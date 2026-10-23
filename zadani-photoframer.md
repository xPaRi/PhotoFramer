# Zadání: PhotoFramer – automatizace úprav dokumentárních fotografií

Dokument odpovídá stavu aplikace k 23. 10. 2026.

## Kontext

Dokumentární fotografie upravuji stále stejným postupem:

1. Ořez na čtverec (ručně, **přímo v hlavním okně aplikace**)
2. Změna velikosti na 900×900 px
3. Úprava v externí aplikaci – výchozí **Nik Collection – Analog Efex Pro 2** (stará verze Google Nik Collection 2016)
4. Orámování pruhem a barevnými čarami (výsledný obrázek je tedy větší než 900×900)
5. Podpis barvou v pravém dolním rohu pruhu
6. Vzor: `example.jpg` (934×934 px = 900 + 2 × 17 px pruh)

Krok 3 zůstává poloautomatický – aplikace spustí externí aplikaci, já vyberu recept a kliknu na Save.

## Technologie

- **WPF, .NET 10** (`net10.0-windows`), C#, Visual Studio 2026, solution ve formátu `.slnx`
- **CommunityToolkit.Mvvm** (MVVM, `[ObservableProperty]`, `[RelayCommand]`)
- **SkiaSharp** pro změnu velikosti, kreslení rámečku a textu a zápis JPEG a PNG
- TIFF čtení/zápis přes WPF `TiffBitmapEncoder` / `BitmapDecoder` (SkiaSharp neumí TIFF zapsat); proto jádro cílí na `net10.0-windows`
- Konfigurace v JSON (`System.Text.Json`)
- Standardní WPF s vestavěným Fluent tématem (`ThemeMode`, světlý/tmavý režim); Syncfusion se nepoužívá, výběr barvy je vlastní ovládací prvek

## Integrace s externí aplikací (Analog Efex Pro 2)

Ověřené chování (stejně jej volá Lightroom / Zoner Photo Studio):

- 64bitový exe: `C:\Program Files\Google\Nik Collection\Analog Efex Pro 2\Analog Efex Pro 2 (64-Bit)\Analog Efex Pro 2.exe`
- exe se spustí s cestou k souboru jako parametrem: `"Analog Efex Pro 2.exe" "C:\temp\foto.tif"`
- otevře okno pluginu, uživatel upraví a klikne **Save**
- výsledek se uloží **zpět do stejného souboru** (8bitový TIFF, stejné rozměry) a proces se ukončí

Detekce „zrušeno“: pokud se soubor po ukončení procesu nezměnil (čas zápisu, velikost), krok se zruší a zaloguje; uživatel zůstává u ořezu a může to zkusit znovu.

Cesta k externí aplikaci je v nastavení (sekce Zpracování a výstup) a je předvyplněna cestou k aplikaci, která se skutečně použije (automatické hledání `Analog Efex Pro 2*.exe` pod `Nik Collection`, přednost 64-bit). Pokud je pole prázdné nebo soubor neexistuje, zobrazí se varování. Pole je zašedlé, pokud není zatrženo „Upravit v externí aplikaci“. Návrh `NikRunner` umožňuje snadno přidat i jiný exe (`NikPlugin`).

## Pracovní postup v aplikaci

1. **Soubor → Otevřít…** (Ctrl+O), přetažení souboru do okna, **Vložit ze schránky** (Ctrl+V) nebo parametr příkazové řádky (např. z Total Commanderu) načte libovolnou fotku (JPEG, TIFF, PNG) do hlavního okna. Ze schránky lze vložit obrázek, nebo první zkopírovaný soubor.
2. Ruční **čtvercový výřez** přímo ve fotce: tažením se posouvá, rohy nebo kolečkem myši se mění velikost, zobrazuje se rozměr výřezu. Klávesy: šipky posouvají o 1 px, numerické `+` / `−` mění velikost o 1 px, s Shiftem je krok 20 px. Klik do ořezu přebere klávesnici z textového pole.
3. **Soubor → Ořez** (Ctrl+Enter, nebo dvojklik v okně ořezu): ořízne, zmenší na cílovou velikost a (je-li zapnuto) otevře externí aplikaci. Pokud uživatel zavře externí aplikaci bez uložení, zůstává u ořezu.
4. Po ořezu se fotka zobrazuje **rovnou s rámečkem a podpisem** a obraz se překresluje při každé změně nastavení rámečku a podpisu.
5. **Uložit** (Ctrl+S) uloží soubor podle preferencí profilu (formát, výstupní složka, přípona názvu; originál se nikdy nepřepíše). **Uložit jako…** (Ctrl+Shift+S) otevře dialog pro volbu názvu a umístění.
6. **Uložit do schránky** (Ctrl+Shift+C) zkopíruje hotový obrázek s rámečkem a podpisem. Volba „Při uložení do souboru také uložit do schránky“ dělá obojí najednou.
7. **Znovu načíst** (F5, Ctrl+R) načte původní soubor z disku (u obrázku ze schránky se vrací k původnímu obrázku) a vrátí se k ořezu.
8. **Zrušit** se zobrazuje ve stavové liště jen tehdy, když jde probíhající krok přerušit (čekání na externí aplikaci); Esc dělá totéž.

Všechny akce jsou v menu **Soubor** a mají klávesové zkratky; tlačítka pod obrázkem nejsou. Obrázek ze schránky nemá soubor, při ukládání dostane název `schranka_<datum_čas>` s příponou profilu (do výstupní složky, jinak do složky Obrázky).

Zpracování probíhá po jedné fotce; fronta více souborů se nepoužívá (při více parametrech příkazové řádky se otevře jen první). UI zůstává responzivní (async). Změna velikosti, externí aplikace a výřez se projeví až při dalším ořezu, rámeček a podpis vždy.

Pokud je fotka otevřena z příkazové řádky a je zapnuto zavírání okna, okno se po uložení zavře. Cesty předané jako zkrácené názvy 8.3 (např. z Total Commanderu bez uvozovek) se před otevřením převedou na dlouhé, takže výstupní název odpovídá originálu.

Pozor: v Total Commanderu je třeba parametr zadat v uvozovkách (`"%p%n"`), jinak TC u názvů s mezerami předá zkrácený název.

## Rámeček

Kolem fotky:

- **pruh** nastavitelné šířky (výchozí 17 px; výsledná velikost = fotka + 2 × šířka pruhu) a barvy (výchozí bílá, může být i jiná)
- **čáry** – libovolný počet, každá s vlastní vzdáleností od okraje fotky (směrem ven), tloušťkou a barvou (výchozí jedna čára: vzdálenost 2 px, tloušťka 2 px, `#0080FF`)

## Podpis

- text (výchozí „PaRi“), font ze systémových fontů (výchozí Brush Script MT, 15 px), tučné, kurzíva, barva (výchozí `#0080FF`)
- pozice: pravý dolní roh **v pruhu**, odsazení od pravého a dolního okraje (výchozí 14 / 1 px)
- antialiasing, správná diakritika

## Výstup

- formát **JPG** (kvalita 1–100 %, výchozí 95) nebo **PNG** (komprese 0–9, výchozí 6; PNG je bezeztrátové, komprese ovlivňuje jen velikost souboru a rychlost)
- výstupní složka (prázdná = vedle originálu) a přípona názvu (výchozí `_frame`)

## Konfigurace

- JSON v `%APPDATA%\PhotoFramer\settings.json`
- **více pojmenovaných profilů** (výchozí „PaRi“; nový, duplikovat, smazat po kontrolním dotazu, přejmenovat – po kliknutí na Přejmenovat se combobox změní na textové pole)
- profil: cílová velikost, zapnutí externí aplikace, výstupní formát, kvalita JPEG, komprese PNG, výstupní složka, přípona názvu, pruh, čáry, podpis
- aplikace: cesta k externí aplikaci, zavření okna po uložení souboru z příkazové řádky, uložení do schránky při uložení souboru, tmavý režim (výchozí zapnuto), zobrazení logu, vysunutý panel nastavení, poloha a velikost okna
- vstupní pole hlídají hodnoty: číselná pole přijmou jen číslice a mají omezenou délku a rozsah (např. kvalita JPEG 1–100), pole barvy přijme jen `#` a hexadecimální číslice
- log do `%APPDATA%\PhotoFramer\photoframer.log`

## UI (WPF)

- hlavní okno: fotka s výřezem nebo hotový obraz s rámečkem; volitelný log pod ním
- **menu Soubor** (Otevřít, Znovu načíst, Ořez, Uložit, Uložit jako, Vložit ze schránky, Uložit do schránky, Konec)
- **nastavení ve vysouvacím panelu** vpravo, otevírá tlačítko ☰ v řádku s menu (poloha se při otevření nemění); panel má viditelný posuvník
- stavová lišta dole jako ve Visual Studiu: zprávy a průběh, při práci oranžová, s tlačítkem Zrušit, když je krok přerušitelný
- záhlaví okna ukazuje název otevřeného souboru (`soubor – PhotoFramer`)
- tooltipy se zobrazují nad prvkem, aby je nepřekrýval zvětšený kurzor
- výběr fontu ze systémových fontů, vlastní výběr barvy (hex pole, za ním čtvereček s náhledem; okno s posuvníky RGB, jejichž značky mají barvu kanálu)
- světlý / tmavý režim
- aplikace si pamatuje velikost a polohu okna
- ikona aplikace (rámeček s fotkou, modrou linkou a podpisem)
- spustitelný soubor `PhotoFramer.exe`
- chyby schránky (schránku občas drží jiná aplikace) se ošetřují opakováním a srozumitelnou zprávou ve stavové liště

## Kvalita kódu

- MVVM, jádro oddělené od UI v samostatném class library projektu (`PhotoFramer.Core`: `ImagePipeline`, `FrameRenderer`, `NikRunner`, `SettingsService`, `ImageIO`, logování)
- `ImagePipeline` ve dvou krocích: `PrepareAsync` (ořez, zmenšení, externí aplikace) a `SaveFramed` (rámeček, podpis, JPEG/PNG); mezi nimi lze měnit rámeček
- async/await, `CancellationToken` pro zrušení
- logování chyb do souboru
- unit testy (xUnit, 21) pro `FrameRenderer` (rozměry výstupu, pozice čar a podpisu, diakritika), `NikRunner`, `SettingsService`, pipeline (JPG i PNG) a omezení hodnot

## Distribuce a repozitář

- `publish.cmd` zabalí aplikaci do jednoho EXE (`publish\PhotoFramer.exe`): soběstačný (~70 MB), nebo s parametrem `small` malý (vyžaduje .NET 10 Desktop Runtime)
- `README.md` (česky i anglicky), `.gitignore`, solution `PhotoFramer.slnx`
- `tools/IconGenerator` vykreslí ikonu aplikace

## Mimo rozsah

- jiné Nik pluginy (ale návrh `NikRunner` to umožňuje)
- plně automatické ovládání okna externí aplikace (klikání na recept / Save přes UI automation) – případně až jako budoucí rozšíření
- zpracování fronty více souborů
- EXIF orientace a správa ICC profilů
