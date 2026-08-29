# GoZC Condor Launcher

Nederlandstalige Windows-launcher voor de GoZC Condor 3-clubsimulator. De app is gebouwd met C#, .NET 8 en WPF en bevat de 15 bestaande clubscenario's. Versie 1.1 voegt beveiligde instellingen, configureerbare scenariogroepen en automatische versie-informatie toe.

## Publiceren

Installeer op de ontwikkel-pc de .NET 8 SDK en start `publish-windows.bat`. Het script herstelt de dependencies, bouwt de app, voert de pakketvrije tests uit en maakt een self-contained Windows x64-publicatie in:

`bin\Release\net8.0-windows\win-x64\publish`

Kopieer de volledige inhoud van die map naar de simulator-pc. .NET hoeft daar niet apart geïnstalleerd te zijn. `appsettings.json` moet naast de executable blijven staan.

## Eerste start

Bij de eerste start verschijnt automatisch de configuratiewizard. Deze vraagt om:

1. de Condor Main-map met `Condor.exe`;
2. de Condor User-map met `Flightplans` en/of `Pilots`;
3. een bestaand Condor-pilotprofiel.

De wizard probeert gangbare Condor 3-locaties automatisch te herkennen. Bij precies één gevonden programmamap wordt deze vooraf ingevuld. Ook `{Documents}\Condor3` wordt gecontroleerd als gebruikersmap.

Bij een nieuwe installatie vraagt de wizard ook tweemaal om een beheerderswachtwoord van minimaal zes tekens. Het leesbare wachtwoord wordt nooit opgeslagen: de launcher bewaart alleen een unieke salt en een PBKDF2-SHA256-hash met 210.000 iteraties.

De persoonlijke instellingen staan in:

`%LOCALAPPDATA%\GoZC Condor Launcher\user-settings.json`

De app toont de wizard opnieuw wanneer dit bestand ontbreekt of beschadigd is, de configuratie onvolledig is of een vereist hoofdpad niet meer bestaat. Via **Instellingen** rechtsboven kunnen de gekozen mappen later worden aangepast en geopend. Deze knop vraagt altijd eerst om het beheerderswachtwoord; na drie verkeerde pogingen geldt een blokkering van 30 seconden.

In **Scenarionamen** kunnen de zichtbare namen van alle 15 scenario's worden gewijzigd. In **Scenariogroepen** kunnen groepsnamen en hun sorteervolgorde worden aangepast of naar de standaardwaarden worden hersteld. Nummers, vaste `GroupId`-waarden en gekoppelde `.fpl`-bestanden blijven onveranderd. **Beveiliging en versies** bevat de functie om het beheerderswachtwoord te wijzigen, toont de Condor- en launcherversie en biedt een directe knop om `launcher.log` te openen.

### Wachtwoord vergeten

De beheerder kan de launcher sluiten en `%LOCALAPPDATA%\GoZC Condor Launcher\user-settings.json` handmatig hernoemen of verwijderen. Bij de volgende start verschijnt de volledige installatie-wizard opnieuw. De launcher verwijdert of verandert dit bestand nooit automatisch. Noteer vooraf de bestaande Condor- en pilotmappen als die opnieuw moeten worden ingevuld.

## Vlucht starten

Selecteer een scenario en kies **VR-bril** of **Scherm**. Voor het starten controleert de launcher achtereenvolgens `Condor.exe`, het scenario, de pilotmap en `VR.ini` of `Scherm.ini`. Daarna worden bestaande `Flightplan.fpl` en `Setup.ini` geback-upt, worden de gekozen bestanden gekopieerd en wordt Condor gestart.

Als het gekozen configuratiebestand ontbreekt, blijft de bestaande `Setup.ini` onaangeroerd en verschijnt het volledige ontbrekende pad in de foutmelding.

## Scenario's en standaardconfiguratie

`appsettings.json` bevat alleen de standaardwerking en de lijst met scenario's. Persoonlijke computer- en pilotpaden horen uitsluitend in `user-settings.json`. Nieuwe scenario's kunnen aan de JSON-lijst worden toegevoegd zonder programmacode te wijzigen; het genoemde `.fpl`-bestand moet in de ingestelde Flightplans-map staan.

Zet `AutomateCondorMenus` op `false` wanneer Condor wel gestart moet worden, maar **FREE FLIGHT** en **Start flight** handmatig worden gekozen.

## Diagnose en tests

Het logbestand staat in:

`%LOCALAPPDATA%\GoZC Condor Launcher\launcher.log`

De tests zijn zonder externe testpakketten uit te voeren met:

```powershell
dotnet run --project tests\GoZCCondorLauncher.Tests.csproj -c Release
```

Ze controleren mapherkenning, pilotdetectie, veilige instellingen, wachtwoord-hashing en -validatie, migratie, groepsnamen en sortering, rasterkolommen, versie-uitlezing en beschadigde JSON.

## Nog testen op de echte Condor-pc

Controleer op de simulator-pc eenmaal een volledige VR- en schermvlucht, inclusief `FREE FLIGHT`, `Start flight`, de debriefing/afsluitcyclus en de gemaakte back-ups. Controleer daarnaast het hoofdmenu op de werkelijk gebruikte simulatorresolutie en probeer de wachtwoordblokkering en groepsvolgorde praktisch uit.
