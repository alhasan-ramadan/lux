# LUX

**Lightweight Utility for eXternal displays.** Die Helligkeit aller Monitore per Klick aus der Taskleiste, über DDC/CI, also direkt übers Grafikkabel statt über die Tasten am Monitor.

## Bedienung

- **Anheften:** Im Explorer Rechtsklick auf `dist\Lux.exe` › „Weitere Optionen anzeigen“ › „An Taskleiste anheften“.
- **Klick aufs Symbol:** Ein kleines Fenster mit einem Regler pro Monitor, der Hauptmonitor zuerst. Ziehen, Mausrad oder Pfeiltasten.
- **25 % bis 100 %:** setzt alle Monitore auf diese Stufe.
- **„Alle gemeinsam bewegen“:** Ein Regler zieht die anderen mit. LUX merkt sich die Wahl.
- **Schließen:** Klick daneben oder Esc. LUX ist danach beendet; außer beim Bedienen läuft nichts im Hintergrund.
- **Merken:** Beim Schließen speichert LUX den Wert im Monitor (DDC-Befehl „Save Current Settings“) und in den eigenen Einstellungen. Manche Monitore vergessen per Kabel gesetzte Werte trotzdem beim PC-Neustart; dagegen hilft eine Aufgabe bei der Anmeldung mit `Lux.exe --restore` (siehe Kommandozeile).

## Voraussetzung

DDC/CI muss im Menü des Monitors eingeschaltet sein, was meist ab Werk so ist. Eingebaute Laptop-Bildschirme regelt Windows auf anderem Weg; die zeigt LUX nicht.

Getestet mit Acer XV272K LV und Samsung C24F390. Der Samsung antwortet kurz nach einem Befehl manchmal nicht; LUX wiederholt deshalb Lesen und Schreiben und lässt zwischen zwei Befehlen 50 ms Pause. Nach einem Neustart verweigert er das Lesen sogar ganz, bis er den ersten Befehl bekommt. Dann bleibt der Regler trotzdem bedienbar und startet beim zuletzt bekannten Wert, erkennbar am „≈“ vor der Zahl.

## Kommandozeile

Gut für Tastenkürzel: eine Verknüpfung auf `Lux.exe --change -10` anlegen und in deren Eigenschaften eine Tastenkombination setzen.

| Aufruf | Wirkung |
| --- | --- |
| `Lux.exe --set 50` | alle Monitore auf 50 % |
| `Lux.exe --change +10` | alle Monitore 10 Prozentpunkte heller (`-10` dunkler) |
| `Lux.exe --dump` | Monitore und Helligkeit ausgeben |
| `Lux.exe --restore` | die zuletzt in LUX gewählte Helligkeit erneut setzen, ohne erst zu lesen; gedacht für eine Aufgabe „Bei Anmeldung“ mit etwa 20 s Verzögerung |

## Dateien

- `%LOCALAPPDATA%\Lux\settings.json`: ob die Regler gemeinsam laufen und die zuletzt bekannte Helligkeit pro Monitor

## Entfernen

Von der Taskleiste lösen, diesen Ordner und `%LOCALAPPDATA%\Lux` löschen.

## Bauen

```powershell
.\build.ps1
```

Braucht nur das .NET-10-SDK und lädt nichts herunter; das Ergebnis liegt in `dist\Lux.exe`.
