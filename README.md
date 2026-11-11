# MicMute

Liten Windows-app som demper/slår på mikrofonen med en global hurtigtast, og viser indikatorer som alltid ligger øverst på skjermen.

## Installere
Last ned `MicMute-Setup-x.y.z.exe` fra [siste versjon](https://github.com/LordPanzar/MicMute/releases/latest) og kjør den. Den trenger ikke administrator, installerer til `%LOCALAPPDATA%\Programs\MicMute` og har med alt som trengs (.NET følger med). Appen legger seg i systemstatusfeltet (nede til høyre).

**Oppdateringer:** MicMute ser etter nye versjoner på GitHub ved oppstart og noen ganger om dagen, og gir beskjed i systemstatusfeltet. Under **Om** i innstillingene kan du se etter oppdateringer selv og trykke «Oppdater nå» – den nye versjonen lastes ned, installeres og startes automatisk. Automatisk sjekk kan slås av samme sted.

- **Standard hurtigtast:** `Ctrl + Alt + M` (endres i Innstillinger)
- **Venstreklikk på ikonet** i systemstatusfeltet: demp/slå på
- **Dobbeltklikk på ikonet:** åpne innstillinger
- **Høyreklikk på ikonet:** Redigeringsmodus, Innstillinger, Avslutt
- Starter du `MicMute.exe` igjen mens den kjører, åpnes innstillingene

## Styre fra andre programmer (Logitech G HUB, Stream Deck, snarveier)
`MicMute.exe` tar kommandoer som sendes til den som allerede kjører:

| Kommando | Virkning |
|---|---|
| `MicMute.exe --toggle` | Demp / slå på |
| `MicMute.exe --mute` | Demp |
| `MicMute.exe --unmute` | Slå på |
| `MicMute.exe --settings` | Åpne innstillinger |
| `MicMute.exe --exit` | Avslutt |

**Logitech G HUB**, to måter:
1. **F13–F24 (anbefalt):** velg f.eks. F13 under Hurtigtaster → «Tast som ikke finnes på tastaturet». Disse tastene finnes ikke på vanlige tastaturer, så ingen Ctrl/Alt/Shift sendes og ingenting annet utløses – og de virker selv om du holder inne andre taster. G HUB sender dem med et Lua-skript (Profil → Skripting), f.eks.:
   ```lua
   function OnEvent(event, arg)
     if arg == 5 and event == "MOUSE_BUTTON_PRESSED" then PressKey("f13") end
     if arg == 5 and event == "MOUSE_BUTTON_RELEASED" then ReleaseKey("f13") end
   end
   ```
   (bytt `5` med knappenummeret ditt; trykk/slipp gjør at «hold for å snakke» også virker)
   Vanlig tastetrykk-tilordning (TASTER) med en kombinasjon som `Ctrl + Alt + M` virker også, men sender da Ctrl/Alt.
2. **Kommando (gir et kort blink i musepekeren):** Tilordninger → SYSTEM → «Start program», velg `MicMute.exe` og legg til `--toggle` som argument (eller lag en snarvei med argumentet og velg den).

## Indikatorer
Stiler: ikon med bakgrunn, «kun ikon» (bare mikrofonen, uten ramme), tekst («MIKROFON AV»), prikk og skjermramme. Du kan ha så mange du vil, på hvilken som helst skjerm.

Når de er **låst** (vanlig modus) kan du ikke klikke på dem: klikkene går rett gjennom til vinduet bak, og de tar aldri fokus.

**Plassering:** så lenge siden **Indikatorer** i innstillingene er åpen (åpnes også fra menyen «Plasser indikatorer …» eller hurtigtasten for redigering):
- Dra for å flytte (fester seg til skjermkantene)
- Dra i en kant eller et hjørne for å skalere (hold Shift for å beholde forholdet), eller bruk musehjulet
- Høyreklikk for stil, farger, tekst, tykkelse på rammen, synlighet, blinking, «vis også når på», dupliser og slett
- Indikatorene låses igjen når du går til en annen side eller lukker innstillingene

## Innstillinger
Innstillingsvinduet (krever Microsoft Edge WebView2 Runtime, som følger med Windows 11) har fem seksjoner:
- **Generelt:** status, lydsignal (13 lyder inkl. tale og egne .wav-filer, volum, når det spilles), start med Windows, liten melding i hjørnet ved endring
- **Hurtigtaster:** demp/på og redigeringsmodus, F13–F24 for G HUB/makrotaster, og virkemåte: veksle, hold for å snakke eller hold for å dempe
- **Mikrofon:** standardenhet, kommunikasjonsenhet, alle mikrofoner eller flere bestemte enheter, med nivåmåler og inngangsvolum (samme som «Input volume» i Windows) for hver enhet
- **Indikatorer:** forhåndsvisning av skjermene, stil (inkl. «Eget ikon» fra SVG-fil – egen fil for dempet/på, farget med indikatorfargen eller med filens egne farger; filene kopieres til `%APPDATA%\MicMute\icons`), farger, tekst, synlighet, legg til/dupliser/slett


Innstillingene lagres i `%APPDATA%\MicMute\settings.json`.

## Begrensninger
- Dempingen skjer på systemnivå i Windows, så alle programmer får stillhet.
- Spill som kjører i **eksklusiv fullskjerm** tegnes over alle vinduer. Bruk «Vindu uten kantlinje» / «Windowed fullscreen» for at indikatoren skal synes.
- Kjører et spill som administrator, må MicMute også kjøres som administrator for at hurtigtasten skal virke mens spillet er i fokus.

## Bygge og publisere
Krever .NET 8 SDK, [Inno Setup 6](https://jrsoftware.org/isinfo.php) og [GitHub CLI](https://cli.github.com/) (innlogget).

```powershell
.uild.ps1              # app i dist\ + installer i installer\Output.uild.ps1 -NoInstaller # bare appen
```

**Ny versjon:**
1. Øk `<Version>` i `MicMute.csproj` og commit endringene.
2. Kjør `.elease.ps1 -Notes "Hva er nytt"` – bygger, lager taggen `vX.Y.Z`, pusher og legger installeren ut som GitHub-release.

Appene som er installert finner den nye versjonen selv.
