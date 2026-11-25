#!/usr/bin/env python3
"""
RUNIX Database Studio - GitHub Release Notes Generator
Extracts the version changelog section from CHANGELOG.md, generates
download link tables, platform installation notes, and SHA-256 checksums.
"""

import sys
import os
import argparse

def main():
    parser = argparse.ArgumentParser(description="Generate GitHub Release Notes Markdown Body")
    parser.add_argument("--version", required=True, help="Release version (e.g. 1.6.0)")
    parser.add_argument("--repo", default="juniorschroder/Runix", help="GitHub repo owner/name")
    parser.add_argument("--checksums", default="./release-assets/SHA256SUMS.txt", help="Path to SHA256SUMS.txt")
    parser.add_argument("--changelog", default="CHANGELOG.md", help="Path to CHANGELOG.md")
    parser.add_argument("--output", default="release-body.md", help="Output path for markdown notes")
    args = parser.parse_args()

    version = args.version.lstrip("v")
    repo = args.repo

    # 1. Extract changelog section for this version
    changelog_section = ""
    if os.path.exists(args.changelog):
        with open(args.changelog, "r", encoding="utf-8") as f:
            lines = f.readlines()
        target = f"## [{version}]"
        capturing = False
        captured = []
        for line in lines:
            if line.startswith(target):
                capturing = True
                continue
            if capturing and line.startswith("## ["):
                break
            if capturing:
                captured.append(line)
        changelog_section = "".join(captured).strip().rstrip("- \n")

    # 2. Read checksums if available
    checksums_content = ""
    if os.path.exists(args.checksums):
        with open(args.checksums, "r", encoding="utf-8") as f:
            checksums_content = f.read().strip()

    # 3. Assemble document
    doc = []
    doc.append("<div align=\"center\">\n\n")
    doc.append(f"# ⚡ RUNIX Database Studio v{version}\n\n")
    doc.append("**Modern, Cross-Platform & High-Performance Relational Database Studio**  \n")
    doc.append("*Built natively with .NET 10, C# 13, Avalonia UI 11 & SkiaSharp — 100% Offline & Privacy-First*\n\n")
    doc.append("[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-316192?style=flat-square&logo=postgresql&logoColor=white)](https://www.postgresql.org/) ")
    doc.append("[![SQLite](https://img.shields.io/badge/SQLite-07405E?style=flat-square&logo=sqlite&logoColor=white)](https://www.sqlite.org/) ")
    doc.append("[![MySQL](https://img.shields.io/badge/MySQL-4479A1?style=flat-square&logo=mysql&logoColor=white)](https://www.mysql.com/) ")
    doc.append("[![MariaDB](https://img.shields.io/badge/MariaDB-003545?style=flat-square&logo=mariadb&logoColor=white)](https://mariadb.org/) ")
    doc.append("[![SQL Server](https://img.shields.io/badge/SQL_Server-CC292B?style=flat-square&logo=microsoftsqlserver&logoColor=white)](https://www.microsoft.com/sql-server) ")
    doc.append("[![Vard Resilience](https://img.shields.io/badge/Vard-Resilience-F59E0B?style=flat-square)](https://github.com/juniorschroder/vard)\n\n")
    doc.append("</div>\n\n---\n\n")

    if changelog_section:
        doc.append("### 🌟 Destaques & O que mudou nesta Versão\n\n")
        doc.append(f"{changelog_section}\n\n---\n\n")

    base_url = f"https://github.com/{repo}/releases/download/v{version}"
    doc.append("### 📦 Instaladores & Downloads Oficiais\n\n")
    doc.append("| Plataforma | Arquitetura | Pacote Instalador | Descrição |\n")
    doc.append("| :--- | :--- | :--- | :--- |\n")
    doc.append(f"| 🍏 **macOS** | Apple Silicon (M1/M2/M3/M4) | [`Runix-{version}-osx-arm64.dmg`]({base_url}/Runix-{version}-osx-arm64.dmg) | Imagem de disco DMG drag-and-drop |\n")
    doc.append(f"| 🍏 **macOS** | Intel x64 | [`Runix-{version}-osx-x64.dmg`]({base_url}/Runix-{version}-osx-x64.dmg) | Imagem de disco DMG drag-and-drop |\n")
    doc.append(f"| 🪟 **Windows** | x64 (64-bit) | [`Runix-{version}-win-x64-Setup.exe`]({base_url}/Runix-{version}-win-x64-Setup.exe) | Instalador completo Inno Setup |\n")
    doc.append(f"| 🐧 **Linux** | Ubuntu / Debian (x64) | [`runix_{version}_amd64.deb`]({base_url}/runix_{version}_amd64.deb) | Pacote Debian (`sudo dpkg -i`) |\n")
    doc.append(f"| 🐧 **Linux** | Fedora / RHEL / openSUSE (x64) | [`runix-{version}-1.x86_64.rpm`]({base_url}/runix-{version}-1.x86_64.rpm) | Pacote RPM (`sudo rpm -Uvh`) |\n\n")
    doc.append("---\n\n")

    doc.append("### 💻 Instruções de Instalação\n\n")
    doc.append("<details>\n<summary><b>🍏 Dicas para macOS</b></summary>\n\n")
    doc.append("1. Baixe o `.dmg` correspondente à sua arquitetura (Apple Silicon `arm64` ou Intel `x64`).\n")
    doc.append("2. Abra o `.dmg` e arraste o **RUNIX.app** para `/Applications`.\n")
    doc.append("3. Se o macOS Gatekeeper emitir um aviso na primeira inicialização:\n")
    doc.append("   - Clique com o botão direito (ou Control-clique) no `RUNIX.app` em `/Applications` e selecione **Abrir**.\n")
    doc.append("   - Clique em **Abrir** no diálogo de confirmação.\n")
    doc.append("</details>\n\n")

    doc.append("<details>\n<summary><b>🪟 Dicas para Windows</b></summary>\n\n")
    doc.append(f"1. Baixe o instalador `Runix-{version}-win-x64-Setup.exe`.\n")
    doc.append("2. Execute o assistente de instalação e avance até a conclusão.\n")
    doc.append("3. Atalhos no menu Iniciar e na Área de Trabalho serão criados automaticamente.\n")
    doc.append("</details>\n\n")

    doc.append("<details>\n<summary><b>🐧 Dicas para Linux</b></summary>\n\n")
    doc.append("**Debian / Ubuntu / Linux Mint:**\n```bash\n")
    doc.append(f"sudo dpkg -i runix_{version}_amd64.deb\nsudo apt-get install -f\nrunix\n```\n\n")
    doc.append("**Fedora / RHEL / CentOS / openSUSE:**\n```bash\n")
    doc.append(f"sudo rpm -Uvh runix-{version}-1.x86_64.rpm\nrunix\n```\n")
    doc.append("</details>\n\n---\n\n")

    if checksums_content:
        doc.append("### 🔒 Verificação de Integridade (SHA-256)\n\n")
        doc.append("```text\n")
        doc.append(checksums_content)
        doc.append("\n```\n\n")
        doc.append("Para validar a integridade dos arquivos baixados no terminal:\n")
        doc.append("```bash\n# macOS / Linux\nsha256sum -c SHA256SUMS.txt\n\n# Windows PowerShell\nGet-FileHash -Algorithm SHA256 Runix-*.exe\n```\n\n---\n\n")

    with open(args.output, "w", encoding="utf-8") as f:
        f.write("".join(doc))

    print(f"[OK] Release notes body generated successfully at {args.output}")

if __name__ == "__main__":
    main()
