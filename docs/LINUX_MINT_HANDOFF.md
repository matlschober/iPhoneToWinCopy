# Übergabe an Codex auf dem Linux-Mint-Laptop

## Ziel und aktueller Status

Die Finalisierung erfolgt **direkt auf einem dedizierten Linux-Mint-Laptop**. Diese Vorbereitung wurde auf dem Windows-Entwicklungs-PC erstellt. Es wurde kein Linux/WSL auf diesem PC installiert. Ein erfolgreicher Cross-Build ist kein Test unter Mint.

Die vorhandene, ausgelieferte Windows-Version bleibt als WPF-Anwendung unter `src/PocketBridge` erhalten. Der neue Linux-Client ist eine **Avalonia-Vorschau (0.5.0-preview.1)**, keine freigegebene Linux-Ausgabe. Er verwendet denselben Empfangsserver, dieselbe iPhone-Seite und dieselbe Daten-/Quotenverwaltung wie Windows.

Bereits vorbereitet:

- `src/PocketBridge.Core`: eigenständiges `net10.0`-Projekt, ohne WPF oder Avalonia.
- `src/PocketBridge.Desktop`: native Avalonia-11.3.23-Oberfläche mit QR-Kopplung, Eingang/Suche, Vorschau, Kopieren, Original-Speichern, Löschen, Pause, Kopplungsschlüssel, Einstellungen und Start-/Ende-Aufräumdialog.
- Portabler freier-Laufwerksspeicher: Win32 auf Windows, Mount- und Symlink-Auswertung auf Linux.
- Betriebssystemgerechte Pfadvergleiche; Linux-Pfade sind case-sensitive.
- XDG-Datenpfad: `$XDG_DATA_HOME/pocketbridge`, sonst `~/.local/share/pocketbridge`. Darin `settings.json`, `pairing.key` und standardmäßig `inbox/`.
- Neue Linux-Datenordner werden nur für den Benutzer angelegt (0700). Beim Umziehen werden vorhandene Daten beibehalten; fremde, nicht leere Zielordner werden abgewiesen.
- Lokale Linux-Sprachansage über `espeak-ng`, alternativ `spd-say`; der optionale Prozess wird mit ArgumentList und ohne Shell gestartet.
- Portabler HTTP-/Persistenz-Prüflauf unter `tests/PocketBridge.Core.Checks`. Der Test teilt den Szenarioquellcode mit den Windows-Tests; WPF-Teile sind bedingt ausgeklammert.
- `PocketBridge.slnx` enthält ausschließlich die portablen Projekte. WPF muss auf Mint nicht gebaut werden.

## Auf dem Laptop beginnen

1. Das Quellcode-ZIP vollständig in einen beschreibbaren Projektordner entpacken. Windows-`bin/`, `obj/`, `.tools/` und persönliche Daten sind nicht enthalten. Den Projektordner in Codex öffnen und zuerst `AGENTS.md` lesen lassen.
2. Mint-Version, Architektur und Desktop-Sitzung tatsächlich ermitteln:

   ```bash
   cat /etc/os-release
   uname -m
   printf '%s\n' "$XDG_CURRENT_DESKTOP" "$XDG_SESSION_TYPE" "$LANG"
   bash scripts/doctor-linux.sh
   ```

   Primäres Ziel ist ein x64-Mint-Desktop, üblicherweise Cinnamon mit X11 oder XWayland. Nicht voraussetzen, welche Mint-Version auf dem Laptop installiert ist.

3. .NET 10 SDK projektlokal installieren und Pakete wiederherstellen:

   ```bash
   bash scripts/setup-linux.sh
   ```

   Dies lädt das offizielle `dotnet-install.sh`, installiert nach `.tools/dotnet` und setzt für die Projektbefehle den passenden SDK-Pfad. Es verändert keine systemweite .NET-Installation und installiert keine APT-Pakete. Internetzugang ist für SDK und erstes NuGet-Restore nötig. `global.json` verlangt .NET 10, mindestens Feature-Band 10.0.100, mit Roll-forward auf aktuelle 10.0-Feature-Bands.

   Die Skripte finden das lokale SDK selbst. Falls die IDE bzw. C# Dev Kit es nicht findet, deren Prozess vollständig schließen und aus einer Shell mit Projektumgebung starten:

   ```bash
   source scripts/env.sh
   code .
   ```

   `code` steht hier für eine entsprechend installierte VS-Code-IDE; es wird durch diese Übergabe weder eine IDE noch Codex installiert.

4. Falls die Diagnose native Bibliotheken vermisst, die Pakete passend zur tatsächlich installierten Mint-/Ubuntu-Basis ergänzen. Übliche X11-Voraussetzungen:

   ```bash
   sudo apt update
   sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libfreetype6 libxcb1
   ```

   Cinnamon bringt vieles bereits mit. Die systemeigene GTK3-/D-Bus-Portal-Integration und die von .NET benötigten ICU-/OpenSSL-Bibliotheken müssen vorhanden sein. Paketnamen hängen von Mint 21/22 und der Ubuntu-Basis ab; nicht pauschal ein anderes Distributions-Repository registrieren.

5. Optional für die Sprachausgabe:

   ```bash
   sudo apt install espeak-ng
   ```

   Ohne Sprachprogramm funktionieren Empfang und UI weiter; die Anwendung zeigt einen Hinweis. Sprache: Desktop-Locale Deutsch → Deutsch, sonst Englisch. Die Windows-SAPI-Implementierung wird unter Linux nicht geladen.

6. In einer **angemeldeten grafischen Mint-Sitzung** bauen und starten:

   ```bash
   bash scripts/check-linux.sh
   bash scripts/start-linux.sh --data-dir /tmp/pocketbridge-qa --port 47835
   ```

   Die isolierte QA-Instanz ist unter Port 47835 erreichbar; ihr QR-Code enthält diesen Port. Für den normalen Betrieb:

   ```bash
   bash scripts/start-linux.sh
   ```

   Normaler Port ist 47831. Eine zweite Desktop-Instanz mit demselben Datenordner wird über eine Sperrdatei verhindert. Tests nutzen ihre eigenen temporären Ordner und Port 47832.

## Abnahme auf Mint – konkret zu prüfen und fertigzustellen

### Empfang und Persistenz

- Mit einem echten iPhone im selben LAN QR-Code scannen; Bildauswahl, Bild-Einfügen, Text/Links und mehrere Fotos senden.
- Bei mehreren Netzwerkadaptern die richtige IPv4-Adresse auswählen. Firewall und WLAN-Geräteisolation prüfen. Kein automatisches Öffnen der Firewall; falls UFW aktiv ist, gezielt nur das vertrauenswürdige LAN für den gewählten TCP-Port freigeben.
- Empfangsrahmen standardmäßig 512 MiB; keine getrennten Foto-/Text-/Anzahl-Limits. Exakte Restkapazität, Unicode-Text, Batch-Abweisung und volle Quote prüfen. Serverentscheidung ist verbindlich, auch wenn die iPhone-Anzeige noch alte Restkapazität zeigt.
- App schließen und neu starten; Einträge und Kopplung bleiben erhalten. Löschen/Behalten/Abbrechen sowie Speicheranzeige und Suchzähler `Treffer/Gesamt` prüfen. Bei Abbrechen muss der Empfang wieder laufen.
- Speicher auf ein echtes zweites Laufwerk verschieben: Bytes und Metadaten müssen erhalten bleiben. Falsche/volle/gesperrte Ziele müssen den bisherigen Speicherort erhalten. Symlinks, Leerzeichen, Umlaute, Groß-/Kleinschreibung und ein nicht verfügbares Laufwerk testen.

### Mint-Desktop-Integration

- Fenster, Skalierung/HiDPI, Tastaturfokus, Dialoge und Mindestgröße auf dem Laptop prüfen. Der Linux-Client ist eine portierte Arbeitsoberfläche; visuelle Anpassungen an Cinnamon gehören zur Finalisierung.
- Text-Zwischenablage in einem anderen nativen Programm prüfen.
- Bild-Zwischenablage mit z. B. LibreOffice, GIMP und Browser prüfen: tatsächliches Bild in voller Auflösung, nicht nur ein Dateipfad. Avalonia `SetBitmapAsync` verwendet das native Clipboard. **Besonders die Lebensdauer nach dem Schließen der App mit dem tatsächlich vorhandenen Clipboard-Manager prüfen**; Linux hat keine allgemeine Windows-`FlushAsync`-Entsprechung.
- GTK-/Portal-Dateidialoge und Ordnerauswahl auf echten Dateien testen. Original-Speicherung per SHA-256 mit dem gesendeten Bild vergleichen. Vorhandene längere Zieldateien dürfen keinen alten Restinhalt behalten.
- HEIC-/WebP-Verfügbarkeit über Skia auf Mint prüfen. Fehlender Decoder muss einen lesbaren Hinweis und weiterhin Original-Speichern ergeben. Keine Windows-Codecs voraussetzen.
- **EXIF-Drehung und Spiegelung** für typische iPhone-JPEGs prüfen. Die Windows-App hat dafür eine eigene WPF-Implementierung; die neue Skia-/Avalonia-Pipeline muss gegebenenfalls noch entsprechend angepasst werden. Auch GIF/TIFF (erstes Bild), ungewöhnliche Farbräume und die 80-Megapixel-Vorschaugrenze testen.
- Sprachansage tatsächlich anhören: Anzahl/Größe, Deutsch/Englisch, deaktivierter Schalter, Testknopf, mehrere Uploads. `espeak-ng` wird bevorzugt. Falls `spd-say` verwendet wird, **das Stoppen bereits an Speech Dispatcher übergebener Ansagen** bei Ausschalten/Beenden prüfen und ggf. backendgerecht ergänzen. Die aktuelle Prozessverwaltung allein garantiert dieses Verhalten beim Daemon-Fallback nicht.
- Aktuelle Locale-Erkennung nutzt `LANGUAGE`, `LC_ALL`, `LC_MESSAGES`, `LANG`. Mit den tatsächlichen Cinnamon-Spracheinstellungen vergleichen; falls nötig Desktop-Präferenzen ergänzen.

### Build und Paket

```bash
bash scripts/check-linux.sh
bash scripts/publish-linux.sh linux-x64
./artifacts/linux/linux-x64/PocketBridge.Desktop --data-dir /tmp/pocketbridge-published-qa --port 47836
```

Dieser Publish ist ein selbständig laufender **Preview-Ordner**. Vor einer Linux-Auslieferung native Tests abschließen, alle Lizenzhinweise der zusätzlichen Avalonia-/Skia-/Font-Abhängigkeiten beilegen und ein Linux-Paket mit korrekten Ausführungsrechten erzeugen. Die vorhandene `Release.ps1` erstellt ausschließlich das etablierte Windows-Paket und soll nicht für Linux verwendet werden. Startmenü-`.desktop`-Datei, Symbol und optional `.deb`/AppImage sind nächste Packaging-Aufgaben. ARM64 ist nur als Buildziel vorbereitet, nicht als geprüftes Gerät zugesagt.

Die bestehende Windows-ZIP `PocketBridge-0.4.0-win-x64.zip` nicht überschreiben. Änderungen am Core müssen weiter kompatibel zur Windows-WPF-Anwendung bleiben. Auf Windows lautet der Regressionstest:

```powershell
.\.tools\dotnet\dotnet.exe run --project tests\PocketBridge.Checks -c Release
```

Builds und Tests nacheinander ausführen, weil sie gemeinsame Ausgabedateien verwenden. Im Abschlussbericht tatsächliche Mint-Version, Sitzung, bestandene Prüfungen und noch offene Punkte nennen. Einen Cross-Build auf Windows ausdrücklich als solchen benennen.

Optionaler Browsercheck der gemeinsamen iPhone-Seite (Node.js erforderlich):

```bash
npm install --prefix .tools/browser playwright --no-audit --no-fund
node .tools/browser/node_modules/playwright/cli.js install chromium
node tests/browser-paste.cjs
```

Beim ersten Browserstart ggf. die vom Playwright-Diagnosetext geforderten Systembibliotheken für die konkrete Mint-Version ergänzen. Diese Browserpakete sind nur QA-Werkzeuge und gehören nicht in das ausgelieferte App-Paket.

## Quellen

- [Avalonia Linux-Plattformintegration](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)
- [Avalonia Clipboard](https://docs.avaloniaui.net/docs/how-to/clipboard-how-to)
- [Verwendete Clipboard-API in Avalonia 11.3.23](https://github.com/AvaloniaUI/Avalonia/blob/11.3.23/src/Avalonia.Base/Input/Platform/ClipboardExtensions.cs)
- [Microsoft .NET unter Ubuntu](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install)
- [XDG-Verzeichnisstandard](https://specifications.freedesktop.org/basedir-spec/latest/)

## Startprompt für Codex auf Mint

> Lies AGENTS.md und docs/LINUX_MINT_HANDOFF.md. Finalisiere die vorhandene PocketBridge.Desktop-Vorschau direkt auf diesem Linux-Mint-Laptop. Prüfe zuerst OS, Architektur, Desktop-Sitzung und SDK. Verwende isolierte QA-Daten und die vorhandenen Skripte. Führe die native Abnahmeliste aus, behebe beobachtete Fehler, erhalte die Windows-App und dokumentiere tatsächlich bestandene Tests. Stelle danach ein geprüftes Linux-x64-Paket mit Lizenzhinweisen und Startanleitung bereit. Lösche keine vorhandenen persönlichen Eingänge ohne meine Auswahl im App-Dialog.
