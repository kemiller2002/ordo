import json
M = json.load(open("/home/user/state-directed-engineering/site/data/evidence.json"))
exps = {e["id"]: e for e in M["experiments"]}
out = {
  "$comment": (
    "Internal claim-to-source map. Generated from site/data/evidence.json by "
    "scripts/claim-sources — do not hand-edit. The public site does not render a "
    "provenance link on every figure; this file is how a maintainer or an auditor "
    "checks one anyway. Each entry names the artifact the number came from, the "
    "experiment it belongs to, and what the number does not establish."
  ),
  "schemaVersion": "1.0.0",
  "metrics": {},
  "externalReferences": {},
}
for m in M["metrics"]:
    e = exps[m["experiment"]]
    s = m["source"]
    out["metrics"][m["id"]] = {
        "claim": m["label"] + ": " + m["value"],
        "definition": m["definition"],
        "measurement": m["measurement"],
        "provenance": m["provenance"],
        "doesNotEstablish": m["limitation"],
        "experiment": {"id": e["id"], "title": e["title"], "status": e["status"]},
        "source": {
            "repository": s["repository"],
            "path": s["path"],
            "ref": s.get("ref"),
            "section": s.get("section"),
        },
    }
for r in M["references"]:
    out["externalReferences"][r["id"]] = {
        "title": r["title"], "author": r["author"], "year": r["year"],
        "url": r["url"], "finding": r["finding"], "doesNotEstablish": r["limitation"],
    }
p = "/home/user/state-directed-engineering/site/data/claim-sources.json"
json.dump(out, open(p, "w"), indent=2, ensure_ascii=False)
open(p, "a").write("\n")
print("metrics mapped:", len(out["metrics"]), "| external refs:", len(out["externalReferences"]))
