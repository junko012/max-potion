#!/usr/bin/env python3
"""Arma maxpotion_<v>_amd64.deb y maxpotion-<v>-1.x86_64.rpm a partir de la salida de `dotnet publish`.

Uso:  python3 scripts/build_linux_packages.py <versión> <carpeta_publish> <carpeta_salida>
      python3 scripts/build_linux_packages.py --preview <versión>      (solo muestra control / descripción)
Los textos salen de packaging/package.conf y packaging/description.txt.
"""
import os, pathlib, re, shutil, subprocess, sys, textwrap

ROOT = pathlib.Path(__file__).resolve().parent.parent
PK = ROOT / "packaging"

def conf():
    c = {}
    for line in (PK / "package.conf").read_text(encoding="utf-8").splitlines():
        if line.strip() and not line.lstrip().startswith("#") and "=" in line:
            k, v = line.split("=", 1); c[k.strip()] = v.strip()
    return c

def description_paragraphs():
    """Lista de párrafos; cada uno es ('text', str) o ('list', [items])."""
    out, cur = [], []
    for line in (PK / "description.txt").read_text(encoding="utf-8").splitlines() + [""]:
        if line.strip():
            cur.append(line.rstrip())
        elif cur:
            out.append(cur); cur = []
    res = []
    for p in out:
        if all(l.lstrip().startswith("- ") for l in p) or (len(p) > 1 and p[1].lstrip().startswith("- ")):
            head = " ".join(l for l in p if not l.lstrip().startswith("- "))
            res.append(("list", (head, [l.lstrip()[2:] for l in p if l.lstrip().startswith("- ")])))
        else:
            res.append(("text", " ".join(p)))
    return res

def deb_version(v): return v.replace("-", "~")

def deb_description(c):
    lines = [f"Description: {c['summary']}"]
    first = True
    for kind, body in description_paragraphs():
        if not first: lines.append(" .")
        first = False
        if kind == "text":
            lines += [" " + l for l in textwrap.wrap(body, 78)]
        else:
            head, items = body
            if head: lines.append(" " + head)
            for item in items:
                w = textwrap.wrap(item, 74)
                lines.append("  - " + w[0]); lines += ["    " + l for l in w[1:]]
    return "\n".join(lines)

def rpm_description():
    out = []
    for kind, body in description_paragraphs():
        if kind == "text":
            out.append("\n".join(textwrap.wrap(body, 78)))
        else:
            items = ["\n".join(textwrap.wrap(i, 76, initial_indent="- ", subsequent_indent="  ")) for i in body[1]]
            out.append("\n".join(([body[0]] if body[0] else []) + items))
    return "\n\n".join(out)

def deb_control(c, version, size_kb):
    return "\n".join([
        f"Package: {c['name']}", f"Version: {deb_version(version)}", "Architecture: amd64",
        f"Maintainer: {c['maintainer']}", f"Installed-Size: {size_kb}",
        f"Depends: {c['deb_depends']}", f"Section: {c['section']}", f"Priority: {c['priority']}",
        f"Homepage: {c['homepage']}", deb_description(c)]) + "\n"

def rpm_spec(c, version, stage):
    v = version.replace("-", "~")
    return f"""# Generado por scripts/build_linux_packages.py: no editar a mano, editar packaging/
%global debug_package %{{nil}}
%global __strip /bin/true
%global __os_install_post %{{nil}}
%define _build_id_links none
AutoReqProv:    no

Name:           {c['name']}
Version:        {v}
Release:        1
Summary:        {c['summary']}
License:        {c['license']}
Vendor:         {c.get('creator', '')}
Packager:       {c['maintainer']}
URL:            {c['homepage']}
BuildArch:      x86_64
""" + "".join(f"Requires:       {r.strip()}\n" for r in c["rpm_requires"].split(",")) + "".join(f"Recommends:     {r.strip()}\n" for r in c.get("rpm_recommends", "").split(",") if r.strip()) + f"""
%description
{rpm_description()}

%install
mkdir -p %{{buildroot}}
cp -a {stage}/. %{{buildroot}}/

%files
%defattr(-,root,root,-)
%dir /usr/lib/{c['name']}
/usr/lib/{c['name']}/*
/usr/bin/{c['name']}
%dir /usr/share/{c['name']}
/usr/share/{c['name']}/*
/usr/share/applications/{c['name']}.desktop
/usr/share/icons/hicolor/256x256/apps/{c['name']}.png
%doc /usr/share/doc/{c['name']}
"""

def make_stage(c, pub, stage):
    n = c["name"]
    if stage.exists(): shutil.rmtree(stage)
    lib = stage / "usr/lib" / n; share = stage / "usr/share" / n
    lib.mkdir(parents=True); (share / "lang").mkdir(parents=True)
    shutil.copy(pub / "maxpotion", lib / "maxpotion"); os.chmod(lib / "maxpotion", 0o755)
    (stage / "usr/bin").mkdir(parents=True)
    os.symlink(f"../lib/{n}/maxpotion", stage / "usr/bin" / n)
    shutil.copy(pub / "pokemon.db", share / "pokemon.db")
    for f in (pub / "lang").glob("*.json") if (pub / "lang").exists() else []:
        shutil.copy(f, share / "lang" / f.name)
    (stage / "usr/share/applications").mkdir(parents=True)
    shutil.copy(PK / "maxpotion.desktop", stage / "usr/share/applications" / f"{n}.desktop")
    icon = stage / "usr/share/icons/hicolor/256x256/apps"; icon.mkdir(parents=True)
    shutil.copy(ROOT / "src/Exxeguttor.UI/Assets/maxpotion.png", icon / f"{n}.png")
    doc = stage / "usr/share/doc" / n; doc.mkdir(parents=True)
    for f in ("LICENSE", "THIRD-PARTY-NOTICES.md"):
        if (ROOT / f).exists(): shutil.copy(ROOT / f, doc / f)
    (doc / "copyright").write_text(
        f"{c['title']} - {c['homepage']}\nLicense: {c['license']}\nThe full text is in /usr/share/doc/{n}/LICENSE "
        f"(GPL-3). Third-party components: THIRD-PARTY-NOTICES.md.\n", encoding="utf-8")

def main(argv):
    c = conf()
    if len(argv) >= 3 and argv[1] == "--preview":
        v = argv[2]
        print("===== .deb  (apt show) =====");  print(deb_control(c, v, 0).replace("Installed-Size: 0\n", ""))
        print("===== .rpm  (rpm -qi / zypper info) =====")
        print(f"Name        : {c['name']}\nVersion     : {v.replace('-', '~')}\nRelease     : 1\nSummary     : {c['summary']}\n"
              f"License     : {c['license']}\nURL         : {c['homepage']}\nRequires    : {c['rpm_requires']}\nRecommends  : {c.get('rpm_recommends', '')}\nDescription :\n{rpm_description()}")
        return 0
    if len(argv) != 4:
        print(__doc__); return 2
    version, pub, out = argv[1], pathlib.Path(argv[2]).resolve(), pathlib.Path(argv[3]).resolve()
    for need in ("maxpotion", "pokemon.db"):
        if not (pub / need).exists(): print(f"falta {need} en {pub}"); return 1
    out.mkdir(parents=True, exist_ok=True)
    work = out / "_pkgwork"; stage = work / "root"
    make_stage(c, pub, stage)
    built = 0
    if shutil.which("dpkg-deb"):
        size_kb = int(subprocess.check_output(["du", "-sk", str(stage)]).split()[0])
        (stage / "DEBIAN").mkdir()
        (stage / "DEBIAN/control").write_text(deb_control(c, version, size_kb), encoding="utf-8")
        target = out / f"{c['name']}_{deb_version(version)}_amd64.deb"
        subprocess.run(["dpkg-deb", "--root-owner-group", "-Zxz", "--build", str(stage), str(target)], check=True)
        shutil.rmtree(stage / "DEBIAN"); print("OK ", target); built += 1
    else:
        print("dpkg-deb no encontrado: se omite el .deb")
    if shutil.which("rpmbuild"):
        top = work / "rpm"; spec = work / f"{c['name']}.spec"
        spec.write_text(rpm_spec(c, version, stage), encoding="utf-8")
        subprocess.run(["rpmbuild", "-bb", "--define", f"_topdir {top}", "--define", "_rpmfilename %%{NAME}-%%{VERSION}-%%{RELEASE}.%%{ARCH}.rpm",
                        str(spec)], check=True)
        for f in top.rglob("*.rpm"): shutil.copy(f, out / f.name); print("OK ", out / f.name)
        built += 1
    else:
        print("rpmbuild no encontrado: se omite el .rpm")
    shutil.rmtree(work, ignore_errors=True)
    return 0 if built else 1

if __name__ == "__main__":
    sys.exit(main(sys.argv))
