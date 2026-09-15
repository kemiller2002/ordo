/// Filesystem primitives, kept in one module so that every read and write
/// this tool performs is auditable in a single place.
///
/// Two rules are enforced here rather than at call sites:
///
///   * symlinks under a managed directory are refused, never followed and
///     never silently skipped — following one would let a link inside .sde/
///     redirect a write outside it, and skipping one would let a file the
///     manifest declares appear to be intact when it is not;
///   * bytes are copied verbatim. No newline or encoding translation is
///     performed anywhere, because a managed file's SHA-256 is taken over
///     its exact bytes on disk and any rewriting would make integrity
///     checking depend on which operating system installed the file.
module Sde.Core.FileSystem

open System
open System.IO
open System.Security.Cryptography

/// SHA-256 over the exact bytes on disk, lowercase hex.
let sha256File (path: string) : string =
    use stream = File.OpenRead path
    use algorithm = SHA256.Create()
    Convert.ToHexString(algorithm.ComputeHash stream).ToLowerInvariant()

let sha256Bytes (bytes: byte[]) : string =
    use algorithm = SHA256.Create()
    Convert.ToHexString(algorithm.ComputeHash bytes).ToLowerInvariant()

let private isSymlink (info: FileSystemInfo) =
    info.Attributes.HasFlag FileAttributes.ReparsePoint

/// Recursively lists regular files under `dir` as sorted manifest paths.
/// Reports a symlink as an error instead of including or skipping it.
let listManagedFiles (dir: string) : Result<string list, string> =
    let results = ResizeArray<string>()

    let rec walk (current: string) (prefix: string) =
        let entries =
            DirectoryInfo(current).GetFileSystemInfos()
            |> Array.sortWith (fun a b -> String.CompareOrdinal(a.Name, b.Name))

        entries
        |> Array.fold
            (fun state (entry: FileSystemInfo) ->
                match state with
                | Error _ -> state
                | Ok() ->
                    let relPath = if prefix = "" then entry.Name else prefix + "/" + entry.Name

                    if isSymlink entry then
                        Error(sprintf "refusing to manage a symlink: %s" relPath)
                    elif entry :? DirectoryInfo then
                        walk entry.FullName relPath
                    else
                        results.Add relPath
                        Ok())
            (Ok())

    if not (Directory.Exists dir) then
        Ok []
    else
        match walk dir "" with
        | Error message -> Error message
        | Ok() -> Ok(results |> List.ofSeq |> List.sortWith (fun a b -> String.CompareOrdinal(a, b)))

let ensureParentDirectory (path: string) =
    let parent = Path.GetDirectoryName path

    if not (String.IsNullOrEmpty parent) then
        Directory.CreateDirectory parent |> ignore

let copyFileInto (source: string) (destination: string) =
    ensureParentDirectory destination
    File.Copy(source, destination, overwrite = true)

let writeFileInto (destination: string) (contents: string) =
    ensureParentDirectory destination
    // UTF-8 with no byte-order mark, LF preserved exactly as given: the
    // caller decides the bytes, this function does not reinterpret them.
    File.WriteAllText(destination, contents, Text.UTF8Encoding false)

let removeTree (dir: string) =
    if Directory.Exists dir then
        Directory.Delete(dir, recursive = true)

let readAllText (path: string) : string = File.ReadAllText path

let fileExists (path: string) : bool = File.Exists path

let directoryExists (path: string) : bool = Directory.Exists path

/// True when the directory exists and this process can create a file in it.
/// Probing by writing is deliberate: permission bits, read-only mounts and
/// container filesystems disagree often enough that only an actual write
/// answers the question `init` needs answered.
let isWritableDirectory (dir: string) : bool =
    try
        let probe = Path.Combine(dir, sprintf ".sde-write-probe-%s" (Guid.NewGuid().ToString "N"))
        use stream = File.Create(probe, 1, FileOptions.DeleteOnClose)
        stream.WriteByte 0uy
        true
    with _ ->
        false
