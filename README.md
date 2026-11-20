# Jalopy Mod Menu

Mod per Jalopy su Windows, sviluppato sulla build a 64 bit con Unity Mono 2017.4.23. Richiede [BepInEx 5.4.23.2 x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2).

## Compilazione su GitHub

1. Estrai l'archivio e metti **il contenuto** della cartella `JalopyModMenu` alla radice del repo. Verifica che GitHub mostri `.github/workflows/build.yml` e `JalopyModMenu.csproj` alla radice.
2. Apri **Actions → Build Jalopy Mod Menu → Run workflow**. Il workflow parte solo manualmente e dev'essere presente sul branch predefinito del repo perché GitHub mostri il pulsante.
3. A compilazione riuscita, apri il run e scarica **JalopyModMenu** da **Artifacts**. L'archivio dell'artifact contiene `JalopyModMenu.dll`.

Actions scarica i riferimenti di Unity e BepInEx. La DLL `Assembly-CSharp.dll` viene individuata direttamente nel gioco quando il mod parte.

## Installazione e comandi

Estrai BepInEx x64 accanto a `Jalopy.exe` e avvia il gioco una prima volta. Metti `JalopyModMenu.dll` in `Jalopy\BepInEx\plugins\` e riavvia. A piedi, dopo che il gioco ti ha dato il controllo, apri il menu con **F7**.

| Tasti | Azione |
| --- | --- |
| F7 | Apri o chiudi il menu |
| PgUp / PgDn | Seleziona una voce |
| Invio | Attiva o disattiva la voce |
| Home / End | Regola la velocità quando selezioni Speed |
| Tasti di movimento del gioco | Spostati |
| Spazio / Ctrl | Sali o scendi con Fly |
| Shift | Raddoppia la velocità |

**Fly** permette di volare nella direzione della visuale. **Noclip** disattiva le collisioni e permette di muoversi orizzontalmente anche da solo. Attivali entrambi per volare attraverso gli ostacoli. Chiudi il menu per muoverti con una modalità attiva. Quando disattivi Noclip dentro un ostacolo, il mod tenta di riportarti in una posizione libera. Le modalità si disattivano quando ti siedi o quando il controller a piedi non è più disponibile.

## Compilazione locale facoltativa

Con .NET SDK 8 e PowerShell, passa i percorsi delle cartelle `Jalopy_Data\Managed` e `BepInEx\core` della tua installazione.

```powershell
dotnet build JalopyModMenu.csproj -c Release "-p:UnityManagedDir=D:\Games\Jalopy\Jalopy_Data\Managed" "-p:BepInExCoreDir=D:\Games\Jalopy\BepInEx\core"
```
