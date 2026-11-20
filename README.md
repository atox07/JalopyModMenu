# Jalopy Mod Menu

Mod per Jalopy su Windows, sviluppato sulla build a 64 bit con Unity Mono 2017.4.23. Richiede [BepInEx 5.4.23.2 x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2).

## Compilazione su GitHub

1. Estrai l'archivio e metti **il contenuto** della cartella `JalopyModMenu` alla radice del repo. Verifica che GitHub mostri `.github/workflows/build.yml` e `JalopyModMenu.csproj` alla radice.
2. Apri **Actions → Build Jalopy Mod Menu → Run workflow**. Il workflow parte solo manualmente e dev'essere presente sul branch predefinito del repo perché GitHub mostri il pulsante.
3. A compilazione riuscita, apri il run e scarica **JalopyModMenu** da **Artifacts**. L'archivio dell'artifact contiene `JalopyModMenu.dll`.

Actions scarica i riferimenti di Unity e BepInEx. La DLL `Assembly-CSharp.dll` viene individuata direttamente nel gioco quando il mod parte.

## Installazione e comandi

Estrai BepInEx x64 accanto a `Jalopy.exe` e avvia il gioco una prima volta. Metti `JalopyModMenu.dll` in `Jalopy\BepInEx\plugins\` e riavvia. La DLL è già inclusa alla radice dello ZIP. Dopo che il gioco ti ha dato il controllo, apri il menu con **F7**.

| Tasti | Azione |
| --- | --- |
| F7 | Apri o chiudi il menu |
| Freccia su / giù | Seleziona una voce |
| Invio | Apri una sezione o usa la voce selezionata |
| Backspace | Torna al menu principale |
| Freccia sinistra / destra | Riduci o aumenta la velocità sulla riga Speed (1–30 m/s) |
| Freccia sinistra / destra nella lista Items | Salta 9 oggetti |
| Tasti di movimento del gioco (WASD di default) | Spostati anche a menu aperto |
| Spazio / Ctrl | Sali o scendi con Fly |
| Shift | Raddoppia la velocità |

**Fly** permette di volare nella direzione della visuale. **Noclip** disattiva le collisioni e permette di muoversi orizzontalmente anche da solo. Attivali entrambi per volare attraverso gli ostacoli. Con una modalità attiva, a menu aperto le frecce servono per il menu e gli altri tasti di movimento restano utilizzabili. Quando disattivi Noclip dentro un ostacolo, il mod tenta di riportarti in una posizione libera. Le modalità si disattivano quando ti siedi o quando il controller a piedi non è più disponibile.

| Sezione | Funzioni |
| --- | --- |
| Vehicle | Ripara i ricambi montati e gli pneumatici, fai il pieno o mantieni pieno il serbatoio installato. |
| Vehicle → Wash Laika | Rimuovi lo sporco da carrozzeria, vetri, pneumatici e coperture degli optional. |
| Vehicle → Max Upgrades | Monta portapacchi, bull bar, paraspruzzi, cruscotto digitale, fari sul tetto e rastrelliera degli attrezzi di livello 3. |
| Vehicle → Paint Laika | Scegli uno dei 14 colori e premi Invio per applicarlo ai pannelli verniciabili. Il colore segue il normale salvataggio della Laika. |
| Vehicle → Car noclip | Attivalo dal posto di guida su terreno solido. Usa i comandi di guida impostati in Jalopy e **Spazio** per frenare. L'auto segue il terreno con le sospensioni in pausa. Quando scendi o disattivi la modalità, tornano la guida e le collisioni del gioco. |
| Items / Give | Sfoglia il catalogo. Seleziona un oggetto e premi Invio per farlo comparire davanti a te, già acquistato e pronto da raccogliere. Usalo a piedi, guardando verso uno spazio libero. |
| Money | Aggiungi 100 o attiva Infinite money per mantenere almeno 10.000 nel portafoglio. |
| World Editor | A piedi e a mani libere, apri la camera indipendente per selezionare e modificare oggetti oppure usa i comandi rapidi di posizionamento. |
| Time Control | Scegli mattina (08:00), mezzogiorno (12:00), tramonto (18:30) o notte (22:00). Freeze time ferma l'orologio del mondo mentre continui a giocare. |
| Weather Control | Scegli Sole, Nebbia, Pioggia o Temporale. Automatic ripristina il meteo della tratta. |

Apri Time Control e Weather Control dal menu **F7** e usa **Invio** per applicare la voce selezionata. Puoi cambiare l'ora anche con l'orologio bloccato. Il meteo usa i profili di luce, suono e precipitazioni di Jalopy. Il temporale aggiunge lampi intermittenti.

### World Editor

La freecam **Open Freecam / Gizmo** seleziona prop, oggetti di scena e la Laika. Usa **Map Builder** per modificare le strade, il terreno, gli edifici e i pezzi piazzati dal catalogo.

Apri **F7 → World Editor → Open Freecam / Gizmo**. Tieni premuto il tasto destro del mouse e usa **WASD** e **Q/E** per volare. Rilascia il destro e seleziona con il sinistro. Il giallo indica l'oggetto puntato e l'azzurro indica quello selezionato.

| Tasti | Azione |
| --- | --- |
| W | Mostra le frecce X/Y/Z, da trascinare con il mouse. |
| R | Mostra gli anelli di rotazione X/Y/Z. |
| S | Mostra i quadratini per scalare gli oggetti lungo gli assi locali. Tieni Shift all'inizio del trascinamento per scalare in modo uniforme. |
| P | Rendi raccoglibile la Laika o una mesh indipendente, anche di grandi dimensioni. Fuori dall'editor usa i normali comandi di raccolta e rilascio del gioco. |
| Ctrl+Z | Annulla l'ultimo trascinamento confermato, o annulla quello in corso. |
| Backspace | Annulla il trascinamento in corso. |
| F7 | Esci dalla freecam e torna al menu. |

Puoi selezionare anche la Laika dal marciapiede. Quando rilasci il gizmo, l'auto si appoggia su terreno solido e riprende la fisica normale. Se la posizione interferisce con la scena o manca terreno sotto le ruote, torna alla posa precedente. I pezzi dei prop sono selezionabili separatamente quando hanno una propria mesh modificabile. Le parti fuse nella geometria della mappa richiedono di separare la mesh.

Ctrl+Z annulla anche scala e conversione dinamica. Le mesh convertite si possono trasportare e lasciare nella scena durante la sessione di gioco. Per trasportare la Laika, selezionala con la freecam, premi **P**, esci con **F7** e torna a giocare a piedi. Punta la carrozzeria e usa il comando di raccolta. Durante il trasporto la Laika resta orizzontale e le sospensioni riprendono a funzionare quando la posi su terreno libero.

| Voce | Azione |
| --- | --- |
| Grab aimed object | Punta un oggetto libero o un prop con una mesh indipendente, poi premi Invio. |
| Spawn and place | Apri il catalogo, scegli un oggetto e passa al posizionamento. |
| Duplicate aimed prop | Crea una copia della geometria e dei materiali del prop con un collider semplificato. |

Durante il posizionamento, muovi la visuale per spostare l'oggetto e usa la rotellina per cambiarne la distanza. **J/L** ruotano in orizzontale e **I/K** inclinano. **Invio** posa, **Shift + Invio** posa e blocca gli oggetti con fisica. Per sbloccarli, riprendili e conferma con **Invio**. **Backspace** annulla, mentre **F7** annulla e riapre il menu.

### Map Builder

Apri **F7 → World Editor → Map Builder** per entrare nella freecam della mappa. Seleziona con il clic sinistro una strada, una sezione di terreno, un edificio modificabile o un pezzo piazzato. **F7** apre il catalogo, dove trovi anche **Open Map Editor** per tornare alla freecam.

Il catalogo usa i prefab delle tratte disponibili e le mesh caricate nella scena. Usa **↑/↓** per scorrere, **←/→** per saltare nove voci e **Invio** per piazzare il modello davanti a te. La freecam della mappa si riapre con il pezzo già selezionato.

Usa **W/R/S** per spostare, ruotare e scalare i pezzi. Per il terreno nativo di Unity, **W** sposta l'intera porzione. **Canc** rimuove i pezzi piazzati e **Ctrl+Z** annulla l'ultima operazione della freecam corrente.

**Save custom map** salva le posizioni, rotazioni e scale dei pezzi piazzati in `JalopyCustomMap.json`, nella cartella dei dati utente del gioco. **Load saved map** rimpiazza i pezzi piazzati con quelli della mappa salvata. Riaprila nella stessa scena per avere a disposizione gli stessi modelli. I pezzi aggiunti usano la geometria, i materiali e le collisioni dei modelli di Jalopy. Le funzioni di traffico, missioni e negozi restano associate alla mappa originale.

## Compilazione locale facoltativa

Con .NET SDK 8 e PowerShell, passa i percorsi delle cartelle `Jalopy_Data\Managed` e `BepInEx\core` della tua installazione.

```powershell
dotnet build JalopyModMenu.csproj -c Release "-p:UnityManagedDir=D:\Games\Jalopy\Jalopy_Data\Managed" "-p:BepInExCoreDir=D:\Games\Jalopy\BepInEx\core"
```
