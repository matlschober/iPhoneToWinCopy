# Verifikation der Linux-Vorbereitung

Stand: 12.11.2026. Ausführungssystem: **Windows 11**, SDK **10.0.404**. Endgültiges Zielgerät: dedizierter Linux-Mint-Laptop des Benutzers.

## Auf Windows ausgeführt

- Portable Solution `PocketBridge.slnx`: Release-Build ohne Warnungen/Fehler.
- Gemeinsame Core-Prüfungen: 49 HTTP-/Persistenz-/Quoten-/Migrations-/Sprach-Szenarien bestanden. Die zusätzlichen Linux-spezifischen Fälle (0700, Symlink, Groß-/Kleinschreibung, XDG) sind vorhanden, werden aber erst unter Linux ausgeführt.
- Windows-WPF-Regression: 54 Szenarien plus native UI-Prüfungen bestanden, einschließlich unveränderter Empfangsabläufe und echter Windows-Sprachsynthese in eine Test-WAV.
- Browserprüfung der gemeinsamen iPhone-Seite bestanden.
- Alle Linux-Shellskripte mit `bash -n` unter Git Bash syntaktisch geprüft. Die Linux-Einrichtungs-/APT-/Publish-Abläufe wurden dadurch **nicht** auf Linux ausgeführt.
- Selbständiger `linux-x64`-Publish von `PocketBridge.Desktop` erfolgreich. Dies ist ein Cross-Build.
- Isolierter Avalonia-Starttest auf Windows mit eigenem QA-Datenordner und Port 47835: Fenster gestartet, gemeinsamer Webserver erreichbar, Text- und Foto-Upload erfolgreich, Fotovorschau angezeigt. Keine persönlichen Eingänge verwendet.

## Noch auf Mint auszuführen

Die native Abnahmeliste in `LINUX_MINT_HANDOFF.md` ist verbindlich offen. Dazu gehören X11/XWayland/Cinnamon, Clipboard und dessen Lebensdauer, native Datei-/Ordnerdialoge, EXIF und Bildcodecs, reale Audioausgabe und Stop-Verhalten, Spracheinstellungen, echte Laufwerke/Symlinks/Berechtigungen, Firewall sowie der iPhone-Ende-zu-Ende-Test im Laptop-WLAN.

Die Windows-Auslieferung 0.4.0 wurde nicht durch einen Linux-Preview-Build ersetzt. Das Quellcode-Handoff ist die Ausgangsbasis für Codex auf dem Laptop, keine Zusage einer bereits abgenommenen Linux-Version.
