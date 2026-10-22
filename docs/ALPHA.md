# Geschlossene Alpha

Leitfaden für den ersten Testlauf mit echten Spielern: was die Alpha umfasst, wie der Realm eingerichtet wird, was Tester tun sollen und wie Rückmeldungen ausgewertet werden.

## Umfang

| Bereich | In der Alpha |
|---|---|
| Stufen | 1–20 (`levelCap` im Manifest) |
| Regionen | Startgebiete Morgentauwald und Aschensteppe, je zwei Hauptstädte, Westmark und Rotsandwüste (Stufe 12–20) |
| Dungeon | Wurzelkrypta (5 Spieler) normal ab Stufe 13 und heroisch ab Stufe 20 mit täglicher Sperre |
| Stufe 20 | Reiten, Tagesquests, Ruf-Rüstmeister mit kosmetischen Reittieren und Titeln |
| Mitspieler | gekennzeichnete KI-Mitspieler (Betriebsart „KI-unterstützt“) oder nur Menschen (`ER_MODE=klassisch`) |
| Sprache | Deutsch |

Nicht in der Alpha: Steam, Bezahlinhalte, Schlachtzüge (10/20/40), Stufen über 20.

## Realm einrichten (Betrieb)

Voraussetzungen und Grundbetrieb stehen in [Betrieb](BETRIEB.md) und [Updates](UPDATES.md). Für die Alpha zusätzlich:

```bash
tools/package.sh /opt/eternal-realms/releases/alpha-1      # Server, Content, Migrationen, Werkzeuge, Textclient, Doku

export ER_DATABASE="Host=localhost;Username=realms;Database=eternal_realms"   # Passwort über ~/.pgpass oder Umgebung, nie im Skript
export ER_INVITE_ONLY=true                                  # neue Konten nur mit Einladungscode
export ER_MOTD="Willkommen zur Alpha von Eternal Realms!\nFehler bitte mit /bug melden – danke!"
export ER_METRICS_FILE=/var/log/eternal-realms/statistik.jsonl   # alle 5 Minuten eine Zeile (ER_METRICS_MINUTES)
export ER_STATUS_FILE=/run/eternal-realms/status
export ER_CONSOLE=true                                      # Spielleitung über die Serverkonsole
dotnet /opt/eternal-realms/current/EternalRealms.ServerHost.dll
```

**Erste Spielleitung:**

1. An der Serverkonsole `einladung 1 Spielleitung` eingeben. Der Code wird **nur dieses eine Mal** angezeigt.
2. Mit dem Code ein Konto anlegen.
3. An der Konsole `rechte <Konto> 2` eingeben.

**Einladungen:**

- `.einladung 20 Welle 1` erzeugt zwanzig Codes mit Notiz. Nur Admins dürfen das.
- `.einladungen` zählt offene und eingelöste Codes.
- Jeder Code gilt genau einmal. In der Datenbank steht nur sein SHA-256-Hash.
- Falsche Codes zählen wie Fehlversuche: Je Adresse sind höchstens 5 Versuche pro Stunde möglich.

**Sicherung vor jedem Test-Wochenende:** `tools/backup.sh` (am besten per cron, siehe Betrieb).

**Zwischen zwei Alpha-Phasen neu beginnen (Wipe):**

```bash
systemctl stop eternal-realms
PGHOST=localhost PGUSER=realms PGDATABASE=eternal_realms ER_WIPE_CONFIRM=eternal_realms ER_STATUS_FILE=/run/eternal-realms/status \
  tools/alpha-wipe.sh                      # ER_WIPE_ACCOUNTS=true löscht zusätzlich alle Spielerkonten
systemctl start eternal-realms
```

- **Was das Skript tut:** Es sichert zuerst die Datenbank. Dann löscht es Charaktere, Gilden, Post, Auktionen, Geldbuch und Weltboss-Zeiten in einer Transaktion.
- **Was erhalten bleibt:** Konten, Meldungen, Fehlermeldungen und Prüfprotokoll.
- **Abbruch:** ohne Bestätigung durch den Datenbanknamen und solange der Server laut Statusdatei läuft.
- **Rückweg:** mit `tools/restore.sh` und der eben erstellten Sicherung.

## Client bauen

Der Unity-Client wird einmal von der Spielleitung gebaut und an die Tester verteilt. Dafür gibt es zwei Wege.

**A) Selbst im Unity-Editor**

1. Unity Hub und **Unity 6 (6000.0 LTS)** installieren. Dazu die Module **Windows Build Support** und gegebenenfalls **Linux Build Support** – das Modul der eigenen Plattform ist immer dabei.
2. Das **.NET-8-SDK** installieren und vor dem ersten Öffnen einmal die gemeinsamen DLLs erzeugen:
   - Windows: `powershell -ExecutionPolicy Bypass -File tools\build-unity-plugins.ps1`
   - Linux/macOS: `tools/build-unity-plugins.sh`
3. `client/Unity` im Unity Hub öffnen. Eine neuere 6000.0-Version darf das Projekt aktualisieren.
4. Menü **Eternal Realms → Alpha-Build → Serveradresse festlegen …**, z. B. `alpha.example.org:7777`, oder leer lassen.
5. Menü **Eternal Realms → Alpha-Build → Windows (64 Bit)**. Danach liegt der Build unter `build/client/<version>-windows/` mit `LIESMICH.txt` für die Tester.

Der Build legt die Startszene selbst an, stellt die Eingabe auf „Both“ und übernimmt Version und Namen aus dem Manifest. Sollten in der Konsole Fehler auftauchen: Bitte die Meldungen kopieren und mir schicken.

**B) Ohne eigene Unity-Installation über GitHub Actions**

Der Workflow „Unity-Build“ läuft nur auf Knopfdruck und erzeugt Windows- und Linux-Builds als Artefakte. Er braucht eine Unity-Lizenz als Repository-Secrets:

1. Unity-Konto anlegen (Unity Personal ist kostenlos).
2. Lizenzdatei (`.ulf`) erzeugen:
   - Einmal Unity Hub installieren, anmelden und eine Personal-Lizenz aktivieren.
   - Die Datei liegt dann unter Windows in `C:\ProgramData\Unity\Unity_lic.ulf`, unter Linux in `~/.local/share/unity3d/Unity/Unity_lic.ulf`.
3. Im Repository unter **Settings → Secrets and variables → Actions** anlegen:
   - `UNITY_LICENSE` mit dem Inhalt der `.ulf`-Datei;
   - `UNITY_EMAIL` und `UNITY_PASSWORD` mit den Daten des Unity-Kontos.
4. **Actions → Unity-Build → Run workflow**, optional mit Serveradresse. Die Builds erscheinen nach dem Lauf unter „Artifacts“.

**Prüfung ohne Editor:** Die CI kompiliert die Client- und Editor-Skripte bei jedem Push gegen die Referenzassemblys von Unity 6000.0.64f1. Lokal geht das mit `tools/fetch-unity-refs.sh` und danach `dotnet build tools/UnityCheck -p:UnityManaged=~/.cache/eternal-realms/unity-6000.0.64f1/Editor/Data/Managed`. Ob der Client im Editor wirklich startet, zeigt aber erst der erste echte Build.

## Für Tester

**Client:**

- **Unity-Client:** Den Build stellt die Spielleitung bereit (siehe „Client bauen“). Die Serveradresse ist dann meist schon eingetragen.
- **Ohne Unity:** Der Textclient liegt im Release unter `client-konsole/`:
  `dotnet EternalRealms.ConsoleClient.dll <server> 7777`, danach `register <konto> <passwort> <Einladungscode>`.
  Mit `hilfe` erscheinen alle Befehle.
- **Konto anlegen:** Im Unity-Client den Einladungscode beim Anlegen des Kontos eintragen. Passwörter brauchen mindestens 8 Zeichen mit Buchstaben und Ziffern und dürfen den Kontonamen nicht enthalten.

**Fehler melden:** im Chat `/bug Text` oder mit Kategorie, z. B. `/bug balance: Der Wolf trifft zu hart`. Im Textclient lautet der Befehl `bug Text`.

- Kategorien: Fehler, Balance, Text, Absturz, Vorschlag.
- Ort, Stufe und Versionen ergänzt der Server selbst. Bitte nur beschreiben, *was* passiert ist und *was* ihr erwartet hättet.
- **Absturz:** Der Client schreibt `fehler.log`. Unity legt die Datei im Ordner `Application.persistentDataPath` ab (unter Windows `%USERPROFILE%\AppData\LocalLow\…\Eternal Realms`), der Textclient neben das Programm. Die Datei bitte zusätzlich an die Spielleitung schicken.

Alle Bilder zeigen unten links „Eternal Realms Alpha …“. Das ist so gewollt: Es ist eine Testversion.

## Testplan

| # | Bereich | Was ausprobieren | Worauf achten |
|---|---|---|---|
| 1 | Anmeldung | Konto mit Code anlegen, abmelden, wieder anmelden; Verbindung kurz trennen | Wiederverbindung innerhalb von 60 s, keine doppelten Charaktere |
| 2 | Stufe 1–12 | Startgebiet mit jeder Klasse anspielen, Tutorial | Questtexte, Wege, Schwierigkeit, verständliche Ziele |
| 3 | Gruppe | Mit KI-Mitspielern und mit anderen Testern gruppieren, Gruppensuche | Bots erkennbar gekennzeichnet, Befehle im Gruppenchat |
| 4 | Wurzelkrypta | Normal mit 5er-Gruppe (gemischt aus Menschen und KI) | Mechaniken (Bodeneffekte, Unterbrechen, Adds), Beute |
| 5 | Stufe 12–20 | Westmark bzw. Rotsandwüste vollständig | Reicht die Erfahrung bis 20 ohne Grinden? Belohnungen passend? |
| 6 | Stufe 20 | Reiten lernen, Tagesquests, heroische Krypta, Ruf-Rüstmeister | Sperre bis 05:00 UTC, Ruf-Fortschritt, Gold-Kreislauf |
| 7 | Wirtschaft | Handel, Post, Auktionshaus, Berufe | Nichts verschwindet oder verdoppelt sich |
| 8 | Technik | Längere Sitzung, viele Spieler an einem Ort | Ruckler, Verbindungsabbrüche, Absturzprotokoll |

## Auswertung

**Rückmeldungen im Spiel lesen:**

| Befehl | Zeigt |
|---|---|
| `.fehler` | offene Fehlermeldungen mit Kategorie, Charakter, Stufe, Ort und Content-Version |
| `.behoben <Nr>` | Fehlermeldung als bearbeitet markieren |
| `.meldungen` | Spielermeldungen |
| `.statistik` | seit dem Start: Spieler online, Stufenverteilung, Tode je Region, Bosskämpfe (Siege, Niederlagen, Dauer), Spielthread-Auslastung |

**Statistik-Datei:** eine JSON-Zeile je Intervall, ohne Chat und ohne Kontodaten.

```bash
# Siege/Niederlagen des heroischen Morrak über den Abend
jq -c '{at, b: .bosses.hboss_morrak}' /var/log/eternal-realms/statistik.jsonl
# Wo sterben die Spieler?
tail -1 /var/log/eternal-realms/statistik.jsonl | jq .deathsByZone
```

**Fehlermeldungen in der Datenbank:**

```sql
SELECT category, count(*) FROM bug_reports WHERE NOT handled GROUP BY category;
SELECT id, at, character_name, zone, level, text FROM bug_reports WHERE NOT handled ORDER BY at DESC LIMIT 50;
```

## Bekannte Grenzen der Alpha

- Der Unity-Client ist gegen die Referenzassemblys von Unity 6 kompiliert, aber noch nicht in einem echten Unity-Editor gestartet. Der erste Build gehört deshalb in die Alpha.
- Grafik und Klang sind prozedural, also Platzhalter ohne eigene Modelle und Musik.
- Die Balance von heroischer Krypta und Stufe 12–20 beruht auf Simulationen mit KI-Gruppen. Genau dafür ist die Alpha da.
- Ein Wipe zwischen den Phasen ist eingeplant. Fortschritt aus der Alpha bleibt nicht garantiert erhalten.
