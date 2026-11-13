# PocketBridge

Eine kleine native Windows-Anwendung, die Fotos und Text vom iPhone über das lokale Netzwerk empfängt. Kein Konto, keine Cloud und kein Electron. WPF stellt den Windows-Eingang dar; Kestrel stellt eine mobile Webseite und einen Upload-Endpunkt bereit. Die iPhone-Seite verwendet ausschließlich HTML, CSS und JavaScript ohne externe Assets oder Dienste.

## Linux Mint: vorbereitete Portierung

Die Windows-Version 0.4.0 bleibt erhalten. Zusätzlich gibt es eine **Linux-Desktop-Vorschau 0.5.0-preview.1** auf Avalonia. Die Finalisierung ist für den dedizierten Linux-Mint-Laptop vorgesehen. Empfang, iPhone-Webseite, Persistenz, Einstellungen und Speicherquote liegen jetzt im gemeinsamen `src/PocketBridge.Core`-Projekt; die bewährte Windows-WPF-Oberfläche bleibt unter `src/PocketBridge`.

Die vollständige Übergabe mit Startprompt für Codex steht in [docs/LINUX_MINT_HANDOFF.md](docs/LINUX_MINT_HANDOFF.md). Auf dem Laptop:

```bash
bash scripts/setup-linux.sh
bash scripts/check-linux.sh
bash scripts/start-linux.sh
```

`PocketBridge.slnx` baut nur die portablen Projekte. `bash scripts/publish-linux.sh` erzeugt einen selbständig lauffähigen Linux-x64-Preview-Ordner. Die tatsächliche Mint-Oberfläche, Clipboard-/Portal-Integration, Bildausrichtung und Audio müssen vor der Linux-Freigabe auf dem Laptop geprüft werden. Linux-Daten liegen standardmäßig unter `~/.local/share/pocketbridge` bzw. `$XDG_DATA_HOME/pocketbridge`. Das projektlokale Windows-SDK aus `.tools` wird nicht auf Linux übernommen; das Setup lädt dort ein passendes .NET-10-SDK.

Ein sauberes Quellcode-ZIP für den Laptop wird mit `./Prepare-LinuxHandoff.ps1` erstellt. Es enthält weder SDKs noch Buildausgaben, Empfangsdaten oder Kopplungsschlüssel.

## Starten

`artifacts\PocketBridge\PocketBridge.exe` starten oder `./Start-PocketBridge.ps1` ausführen. Der veröffentlichte Ordner enthält die benötigte .NET-Laufzeit und kann als Ganzes kopiert werden. Keine Installation oder Administratorrechte für die App erforderlich. Beim Schließen des Fensters endet auch der Webservice.

1. Windows-App geöffnet lassen, iPhone und PC mit demselben vertrauenswürdigen Netzwerk verbinden (PC per Ethernet ist ebenfalls möglich).
2. QR-Code mit der iPhone-Kamera scannen und in Safari öffnen.
3. Fotos auswählen oder Text/Link einfügen und senden.
4. Am PC den Eingang auswählen und **Kopieren** oder **Speichern** verwenden.

### Fotos durch Kopieren / Einfügen senden

Auf dem iPhone ein Foto kopieren (z. B. Fotos → Teilen → Foto kopieren). In PocketBridge im Reiter **Fotos** im Feld **Bild aus der Zwischenablage** lange drücken und **Einfügen** wählen. Das Bild erscheint in der Vorschau und wird mit **An PC senden** übertragen. Auf dem Computer funktioniert auch Strg+V bzw. ⌘V im Foto-Reiter. Mehrere Einfügevorgänge ergänzen die aktuelle Auswahl; der Dateiauswahldialog ersetzt sie.

Die Funktion nutzt das vom Benutzer ausgelöste `paste`-Ereignis und benötigt keinen direkten Zugriff über `navigator.clipboard.read()` (dieser wäre in Safari nur in einem sicheren Kontext wie HTTPS verfügbar). Nicht jede Quell-App legt echte Bilddaten in die Zwischenablage: Ein kopierter Bildlink allein reicht nicht. Safari kann Bilder beim Einfügen in PNG umwandeln; übertragen wird die von der Zwischenablage bereitgestellte Datei, die vom ursprünglichen Fotoformat abweichen kann. Einfügen sendet noch nichts automatisch. [WebKit zu Bilddaten beim Einfügen](https://webkit.org/blog/8170/clipboard-api-improvements/) und [sicherem Kontext für Async Clipboard](https://webkit.org/blog/10247/new-webkit-features-in-safari-13-1/).

Mehrere Netzwerkadapter? Links die erreichbare IPv4-Adresse auswählen. Kein Gast-WLAN mit Geräteisolation verwenden. Für spätere Besuche lässt sich die Seite in Safari über „Teilen → Zum Home-Bildschirm“ ablegen. Der Schlüssel bleibt bis zum Erneuern erhalten; eine geänderte PC-IP erfordert das erneute Scannen. Eine DHCP-Reservierung im Router hält die Adresse für Kurzbefehle stabil.

## Eingang aufräumen und Empfangsspeicher einstellen

Wenn persistierte Fotos oder Texte vorhanden sind, fragt PocketBridge beim **Start** sowie beim **Beenden**, ob der gesamte Eingang gelöscht oder behalten werden soll. Die Abfrage zeigt deutlich den belegten Speicher im Verhältnis zum eingestellten Eingangslimit, z. B. **128,5 MB von 512 MB belegt**; ab einem Eingangslimit von 1 GB erfolgt die Darstellung in GB. **Behalten** ist vorausgewählt. Beim Beenden gibt es zusätzlich **Abbrechen**, um die App weiterzuverwenden. Ein leerer Eingang löst keine Abfrage aus. Vor der Abfrage beim Beenden nimmt der Server keine neuen Uploads mehr an und wartet auf eine laufende Übertragung. Bei „Abbrechen“ wird der vorherige Empfangszustand wiederhergestellt. Die Löschung betrifft ausschließlich Einträge im App-Eingang; separat gespeicherte Dateien, Kopplung und Einstellungen bleiben bestehen.

Über **Einstellungen** oben rechts gibt es genau eine Speichergrenze: **Maximal bereitgestellt (MB)**, standardmäßig **512 MB**. Fotos und Texte teilen sich diesen Empfangsspeicher. Separate Grenzen für Fotoanzahl, Fotogröße, Textzeichen oder die Anzahl der gespeicherten Einträge gibt es nicht. Die Gesamtgröße eines Uploads muss in den verbleibenden Empfangsspeicher passen. Ist der Rahmen überschritten, wird die Übertragung mit einer Meldung über benötigten und verbleibenden Platz abgewiesen, bevor Einträge daraus übernommen werden. Text wird nach seinen tatsächlichen UTF-8-Bytes berechnet. HTTP-Formulardaten werden nicht vom Datenkontingent abgezogen.

MB entsprechen hier MiB (1.048.576 Bytes). **Speichern** übernimmt den Wert ohne Neustart; er bleibt in `%LOCALAPPDATA%\PocketBridge\settings.json` erhalten. **Standardwert wiederherstellen (512 MB)** setzt das Eingabefeld zurück; erst „Speichern“ übernimmt es. Die iPhone-Seite zeigt den verbleibenden Speicher nach dem Neuladen, nach einem Upload oder der nächsten Verbindungsprüfung (ca. 10 Sekunden). Ein laufender Upload behält den bei seinem Beginn geltenden Rahmen. Ein niedrigerer Speicherrahmen löscht keine Daten, sondern blockiert weitere Eingänge, solange kein Platz übrig ist. Alte Konfigurationsdateien behalten ihren Gesamt-Speicherwert; frühere Einzelgrenzen werden ignoriert.

In denselben Einstellungen lässt sich über **Ordner wählen** der Zwischenspeicher verlegen. Im gewählten übergeordneten Ordner wird **PocketBridge-Eingang** angelegt; der tatsächliche Pfad wird vor dem Speichern angezeigt. **Standardordner verwenden** wählt wieder `%LOCALAPPDATA%\PocketBridge\inbox`. Ein neuer Zielordner muss leer sein. Beim Speichern wird der Empfang angehalten, laufende Uploads werden abgewartet, alle Einträge werden kopiert und erst danach der neue Pfad aktiviert und der bisherige Eingang bereinigt. Bei einem Kopierfehler bleibt der bisherige Speicherort aktiv. Konfiguration und Zugangsschlüssel bleiben im Windows-Benutzerprofil. Das gewählte Laufwerk muss beim nächsten Start erreichbar sein; die App meldet andernfalls einen Fehler, statt einen anderen, leeren Eingang zu verwenden.

Die Anzeige des tatsächlich freien und gesamten Laufwerkspeichers aktualisiert sich bei Änderungen und alle fünf Sekunden. Beim Speichern wird erneut geprüft, ob genug Platz für das gewünschte Limit vorhanden ist. Am bisherigen Speicherort wird die bereits belegte Datenmenge berücksichtigt; beim Wechsel muss am Ziel genug Platz für die Übernahme und das gewünschte Limit frei sein. Zu hohe Limits werden mit konkreter Fehlermeldung abgewiesen. Das Limit ist eine Obergrenze und reserviert keinen Speicher auf der Festplatte; andere Programme können den freien Platz später verändern.

Der einstellbare Empfangsspeicher reicht von 1 bis 1.048.576 MB. Die Grenze von 80 Megapixeln für die Windows-Bildvorschau bleibt eine technische Schutzgrenze; das Original kann weiterhin gespeichert werden.

Während einer Suche zeigt der Zähler neben **Eingang** die Anzahl **Treffer/Gesamt**, z. B. `3/12`. Bei leerem Suchfeld erscheint nur die Gesamtzahl.

## Empfang per Sprache bestätigen

In **Einstellungen → Sprachansage** lässt sich die Ansage dauerhaft ein- oder ausschalten (Standard: eingeschaltet). **Ansage testen** spielt eine kurze Vorschau. Nach jeder Übertragung nennt eine Ansage die Anzahl der tatsächlich gespeicherten Einträge und ihre Gesamtgröße, etwa „3 Dateien mit insgesamt 20,7 Megabyte empfangen“. Ein Text zählt als ein Eintrag. Mehrere Fotos innerhalb eines Uploads erzeugen eine gemeinsame Ansage; mehrere einzelne Uploads erzeugen nacheinander jeweils eine Ansage.

Die Sprache folgt der Windows-Anzeigesprache: Deutsch verwendet eine installierte deutsche Stimme, Englisch eine englische. Für weitere Windows-Sprachen und bei fehlender deutscher Stimme wird Englisch verwendet, sofern eine englische Stimme installiert ist. Ohne passende Stimme wird eine Meldung angezeigt; der Empfang funktioniert weiter. Teilweise gespeicherte Übertragungen werden ausdrücklich als teilweise empfangen angekündigt. Abgewiesene Uploads ohne gespeicherte Einträge erzeugen keine Ansage. Beim Ausschalten oder Beenden wird laufende bzw. wartende Sprachausgabe gestoppt.

Verwendet wird die vorhandene lokale Windows-SAPI-Sprachausgabe. Es sind keine zusätzlichen NuGet-Pakete, Sprachmodelle, Cloud-Zugriffe oder Sprachdownloads Bestandteil der App. Die Engine wird erst bei Bedarf geladen. [Microsoft: asynchrone Sprachausgabe](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ms723609(v=vs.85)) · [Ermittlung der Windows-Anzeigesprache](https://learn.microsoft.com/en-us/windows/win32/api/winnls/nf-winnls-getuserpreferreduilanguages).

## Firewall

TCP-Port **47831** muss für das iPhone erreichbar sein. Falls Windows nach Zugriff fragt, nur das vertrauenswürdige private Netzwerk erlauben. Dieses Projekt ändert weder Netzwerkprofil noch Firewall automatisch. Der PC war bei der Einrichtung als **öffentlich** eingestuft; das kann eingehende Verbindungen verhindern.

Wenn das Heimnetz vertrauenswürdig ist, in Windows unter „Netzwerk und Internet → Ethernet/WLAN → Netzwerkprofiltyp“ auf **Privat** stellen. Optional lässt sich anschließend als Administrator eine auf das private lokale Subnetz beschränkte Regel erstellen:

```powershell
New-NetFirewallRule -DisplayName 'PocketBridge (privates LAN)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 47831 -Profile Private -RemoteAddress LocalSubnet
```

Keine Portweiterleitung am Router einrichten.

## Aus dem iPhone-Teilen-Menü

**AirDrop:** Apple beschreibt AirDrop als Übertragung zwischen Apple-Geräten. Ein Windows-11-PC ist kein reguläres AirDrop-Ziel. Das Nachbauen der Apple-Funkprotokolle wäre für diese schlanke Anwendung kein tragfähiger Standardweg.

**Reine Webseite/PWA:** Das Hinzufügen zum Home-Bildschirm registriert unter iOS kein eigenes Web-Share-Target im Teilen-Menü. `navigator.share()` ist die entgegengesetzte Richtung (Webseite teilt an andere Apps).

**Empfohlener Weg: Apple Kurzbefehle.** Ein Kurzbefehl kann im Teilen-Menü erscheinen und per HTTP POST an PocketBridge senden. In der Windows-App zeigt **Einrichtung ansehen** den aktuellen Endpunkt und Schlüssel an. Es wird kein ungeprüfter, unsignierter `.shortcut`-Download angeboten; die folgenden Kurzbefehle werden einmal in Apples App erstellt.

### Kurzbefehl „Foto an PC“

1. Neuer Kurzbefehl, Details → **Im Share-Sheet anzeigen**. Eingabe: Bilder.
2. Für mehrere Fotos: **Mit jedem wiederholen** über die Kurzbefehleingabe.
3. **Bild konvertieren**: Wiederholungselement → JPEG (hohe Qualität). Das macht HEIC ohne Windows-Codec als Bild nutzbar. Der Web-Upload erhält dagegen immer die Originaldatei.
4. **Inhalte von URL abrufen**: `http://PC-IP:47831/api/receive`.
5. Methode **POST**, Header `X-Pocket-Key` mit dem vollständigen Schlüssel aus der App.
6. Anfragetext **Formular**: Feld vom Typ **Datei**, Name `files`, Wert „Konvertiertes Bild“.
7. Wiederholung beenden. Optional Antwort anzeigen. Netzwerkzugriff erlauben, falls iOS fragt.

### Kurzbefehl „Text an PC“

1. Im Share-Sheet für Text und URLs aktivieren.
2. **Inhalte von URL abrufen** mit derselben URL, POST und Header.
3. Anfragetext **Formular**: Feld vom Typ **Text**, Name `text`, Wert „Kurzbefehleingabe“.
4. Optional Antwort anzeigen. Danach kann markierter Text oder ein Safari-Link direkt über „Teilen → Text an PC“ gesendet werden.

Ein eigener iOS-Client mit Share Extension wäre eine spätere, komfortablere Variante; er benötigt Xcode, Signierung und Verteilung. Die Kurzbefehle sind vorbereitet durch den getesteten Server-Endpunkt, aber noch nicht auf einem echten iPhone verifiziert.

Quellen (geprüft 12.11.2026): [Apple AirDrop](https://support.apple.com/en-gb/119857), [Kurzbefehle aus anderen Apps](https://support.apple.com/en-au/guide/shortcuts/apd163eb9f95/ios), [HTTP-Anfragen mit Kurzbefehlen](https://support.apple.com/en-au/guide/shortcuts-mac/apd58d46713f/mac), [Apple Share Extensions](https://developer.apple.com/library/archive/documentation/General/Conceptual/ExtensibilityPG/Share.html), [Web Share Target, Plattformintegration](https://github.com/w3c/web-share-target/blob/main/docs/native.md).

## Daten und Grenzen

- Empfang: JPEG, PNG, HEIC/HEIF, WebP, GIF, BMP, TIFF und Text/URLs. Prüfung anhand des Datei-Headers; keine SVG-/HTML-Ausführung.
- Vorschau und Bild-Zwischenablage verwenden Windows-Bilddecoder. HEIC/WebP benötigen gegebenenfalls installierte Codecs. Ohne Decoder bleibt **Speichern des Originals** verfügbar. GIF/TIFF werden als erstes Bild dargestellt/kopiert; die Originaldatei bleibt vollständig.
- Ein einstellbarer Gesamtrahmen von standardmäßig 512 MiB für empfangene Fotos und Texte. Die Summe neuer Daten muss in den noch freien Rahmen passen. Zu große Bilddimensionen (über 80 Megapixel) werden nicht für die Vorschau dekodiert.
- Vorschaubilder werden auf maximal 2048 Pixel Breite reduziert; Kopieren dekodiert das Original in voller Auflösung. EXIF-Drehung und Spiegelung werden für Vorschau und Zwischenablage berücksichtigt. Speichern kopiert die empfangenen Bytes unverändert (Text als UTF-8).
- Der Eingang bleibt bis zum Löschen am gewählten Zwischenspeicherort bestehen (Standard: `%LOCALAPPDATA%\PocketBridge\inbox`). Speichern entfernt den Eintrag nicht. Der Zugangsschlüssel liegt unter `%LOCALAPPDATA%\PocketBridge\pairing.key` im Windows-Benutzerprofil.
- Das iPhone kann ausschließlich senden und den Empfangsstatus prüfen; es gibt keinen Remote-Endpunkt zum Auslesen des Eingangs oder der PC-Zwischenablage.
- Der QR-Link enthält einen zufälligen 192-Bit-Schlüssel im URL-Fragment. Safari legt ihn lokal ab und sendet ihn als Header. **Zugangsschlüssel erneuern** sperrt bestehende Verbindungen; Kurzbefehle müssen dann aktualisiert werden.
- Die lokale **HTTP-Verbindung ist unverschlüsselt**. Kopplung verhindert unautorisierte Uploads, ersetzt aber keine Transportverschlüsselung. Ausschließlich im vertrauenswürdigen LAN verwenden. HTTPS mit auf dem iPhone vertrauenswürdigem Zertifikat ist ein möglicher nächster Ausbauschritt.
- Bei Teilfehlern kann der Server HTTP 207 mit Anzahl der bereits gespeicherten Einträge zurückgeben. Vor erneutem Senden den PC-Eingang prüfen.
- Kein Autostart, Hintergrunddienst oder Cloud-Telemetrie. Pause verhindert neue Uploads; eine bereits laufende Übertragung kann noch fertig werden.

## Entwickeln und prüfen

.NET 10 SDK erforderlich. Das bei der Entwicklung lokal installierte SDK liegt bei Bedarf unter `.tools\dotnet` (ignoriert). Die Windows-Anwendung verwendet .NET 10 LTS und QRCoder (MIT), Version 1.8.1. Die separate Linux-Vorschau ergänzt Avalonia Desktop, Fluent Theme und Inter-Schrift in Version 11.3.23 einschließlich transitiver Grafikabhängigkeiten.

Verifiziertes lokales SDK: 10.0.404. Der systemweite `dotnet`-Pfad kann auf diesem PC noch SDK 7.0.101 auswählen; die Skripte bevorzugen ausdrücklich das Projekt-SDK. C# Dev Kit benötigt ebenfalls ein auffindbares, unterstütztes SDK. Ziel-Framework und `global.json` dieses Projekts verwenden .NET 10, unabhängig von älteren zusätzlich installierten SDKs.

```powershell
.\.tools\dotnet\dotnet.exe build src\PocketBridge\PocketBridge.csproj -c Release
.\.tools\dotnet\dotnet.exe run --project tests\PocketBridge.Checks -c Release
.\Publish.ps1
```

Für die optionale Browserprüfung der Bild-Einfügefunktion: `npm install --prefix .tools/browser playwright --no-audit --no-fund`, danach `node tests/browser-paste.cjs`. Unter Windows wird der installierte Edge verwendet; unter Linux zuerst `node .tools/browser/node_modules/playwright/cli.js install chromium` ausführen. Der Test verwendet einen isolierten Browser mit simulierten HTTP-Antworten und Clipboard-Ereignissen, prüft die tatsächlich erzeugten Upload-Dateien und verändert den PC-Eingang nicht. Er ersetzt keinen Test des nativen iOS-Einfüge-Menüs.

Die Integrationsprüfung verwendet Port 47832, die nativen UI-Prüfungen Port 47833, jeweils mit isolierten temporären Datenverzeichnissen. Die normale App kann währenddessen weiterlaufen. Geprüft werden Kopplung, Unicode, Mehrfachupload, unveränderte Bytes, Persistenz, Löschen, Empfangskontingent, falsche Dateiformate, Pause, Host-Prüfung und Schlüsselwiderruf sowie die echten WPF-Dialoge für Start, Beenden, Einstellungen und Suchzähler. Die Sprachprüfung wählt eine echte installierte Windows-Stimme, erzeugt die Ansage über den nativen Synthesizer in einer temporären WAV-Datei und prüft deren Audioinhalt, ohne sie über die Lautsprecher abzuspielen.

`Publish.ps1` erstellt eine eigenständig lauffähige x64-Ausgabe. `Publish.ps1 -FrameworkDependent` erzeugt eine kleinere Ausgabe, benötigt aber .NET 10 **Desktop Runtime und ASP.NET Core Runtime** auf dem Ziel-PC. WPF und Kestrel sind bewusst nicht mit .NET Trimming veröffentlicht. Das native UI benötigt keinen Chromium-Prozess; die gebündelte Laufzeit bestimmt den Großteil der Downloadgröße.

Mit `./Release.ps1` wird ein vollständiges portables ZIP-Release unter `artifacts/releases/PocketBridge-<Version>-win-x64.zip` erzeugt. Das Skript baut in einem isolierten temporären Ordner, ergänzt Kurzanleitung, Versionshinweise und die Original-Lizenztexte der tatsächlichen Abhängigkeiten und prüft jede archivierte Datei per SHA-256. Neben dem ZIP entsteht eine `.zip.sha256`-Datei; im Paket liegt zusätzlich ein Datei-Prüfsummenverzeichnis. Persönliche Eingänge, Einstellungen, Kopplungsschlüssel und Debugdateien sind ausgeschlossen. Die laufende Ausgabe unter `artifacts/PocketBridge` wird nicht verändert. Bei Bedarf wird der zur Laufzeitversion passende WPF-Komponentenhinweis von der offiziellen .NET-Quelle geladen.

Messung auf diesem PC: eigenständiger Ausgabeordner ca. **166 MiB** inklusive Laufzeiten, Prozess nach dem Start ca. **142 MiB Working Set** (momentane Messung, kein garantierter Grenzwert). „Schlank“ bezieht sich hier auf den Aufbau ohne Browser-Engine, Frontend-Framework oder Hintergrunddienst; eine Installation von nur wenigen Megabyte ist die eigenständige .NET-Ausgabe nicht.

Verifiziert: automatisierte HTTP-Integration und native WPF-Prüfungen; mobile Oberfläche in Edge/Chromium bei 390 × 844 Pixeln mit Text-/Fotoupload, Kopplung, Wiederverbinden und Empfangskontingent; native Windows-Bild-/Text-Zwischenablage (Bild mit voller Auflösung 1600 × 1000), nativer Speichern-Dialog mit Bytevergleich; native Sprachsynthese als WAV, Sprachwahl und gespeicherter Sprachschalter; Start und HTTP-Empfang aus der eigenständigen Ausgabe. Der Benutzer hat den Transfer und das Einfügen von Fotos auf einem echten iPhone bestätigt. Die neuen Einstellungsänderungen auf der iPhone-Seite wurden automatisiert im Browser geprüft; Kurzbefehle sind weiterhin nicht auf einem echten Gerät verifiziert.
