using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GoZCCondorLauncher.Tests;

[TestClass]
public sealed class LauncherTests
{
    private string _root = null!;
    [TestInitialize] public void Initialize() { _root = Path.Combine(Path.GetTempPath(), $"gozc-tests-{Guid.NewGuid():N}"); Directory.CreateDirectory(_root); }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod] public void CondorMainEnUserWordenGevalideerd()
    {
        var main = Path.Combine(_root, "Condor3"); var user = Path.Combine(_root, "User");
        Directory.CreateDirectory(main); Directory.CreateDirectory(Path.Combine(user, "Flightplans")); File.WriteAllText(Path.Combine(main, "Condor.exe"), "test");
        Assert.IsTrue(ConfigurationService.IsValidCondorMain(main)); Assert.IsTrue(ConfigurationService.IsValidCondorUser(user));
    }

    [TestMethod] public void PilotprofielenWordenGesorteerd()
    {
        var user = Path.Combine(_root, "Profiles"); Directory.CreateDirectory(Path.Combine(user, "Pilots", "Pilot B")); Directory.CreateDirectory(Path.Combine(user, "Pilots", "Pilot A"));
        var profiles = ConfigurationService.FindPilotProfiles(user); Assert.AreEqual(2, profiles.Count); Assert.AreEqual("Pilot A", Path.GetFileName(profiles[0]));
    }

    [TestMethod] public void InstellingenEnGroepsnamenWordenVeiligBewaard()
    {
        var main = Path.Combine(_root, "Main"); var user = Path.Combine(_root, "User"); var pilot = Path.Combine(user, "Pilots", "ClubPilot");
        Directory.CreateDirectory(main); Directory.CreateDirectory(Path.Combine(user, "Flightplans")); Directory.CreateDirectory(pilot); File.WriteAllText(Path.Combine(main, "Condor.exe"), "test");
        var settings = ConfigurationService.CreateUserSettings(main, user, pilot, new Dictionary<int, string> { [1] = "Naam" });
        settings.GroupPreferences = [new GroupPreference { GroupId = "a", DisplayName = "Groep", SortOrder = 1 }];
        var json = Path.Combine(_root, "settings.json"); ConfigurationService.SaveUserSettings(settings, json);
        Assert.IsTrue(ConfigurationService.TryLoadUserSettings(out var loaded, json, false)); Assert.AreEqual("Naam", loaded.ScenarioNames[1]); Assert.AreEqual("Groep", loaded.GroupPreferences[0].DisplayName);
    }

    [TestMethod] public void BeschadigdeJsonRaaktProductielogNiet()
    {
        var json = Path.Combine(_root, "broken.json"); File.WriteAllText(json, "{ kapot");
        var before = File.Exists(Logger.LogPath) ? File.GetLastWriteTimeUtc(Logger.LogPath) : DateTime.MinValue;
        Assert.IsFalse(ConfigurationService.TryLoadUserSettings(out _, json, logErrors: false));
        var after = File.Exists(Logger.LogPath) ? File.GetLastWriteTimeUtc(Logger.LogPath) : DateTime.MinValue;
        Assert.AreEqual(before, after);
    }

    [TestMethod] public void WachtwoordIsGehashtEnValideertCorrect()
    {
        var password = SecurityService.CreatePassword("clubbeheer"); Assert.AreNotEqual("clubbeheer", password.Hash);
        Assert.IsTrue(SecurityService.VerifyPassword("clubbeheer", password)); Assert.IsFalse(SecurityService.VerifyPassword("verkeerd", password));
        var second = SecurityService.CreatePassword("clubbeheer"); Assert.AreNotEqual(password.Salt, second.Salt);
    }

    [TestMethod] public void BestaandeInstellingenMigrerenZonderVerlies()
    {
        var app = new AppSettings { Scenarios = [new Scenario { Number = 1, Name = "Test", Category = "Oud", Aircraft = "LS4", File = "Scenario1.fpl" }] };
        var user = new UserSettings { CondorMainFolder = "bestaand", ScenarioNames = new() { [1] = "Bewaard" } };
        ConfigurationMigration.Migrate(app, user); Assert.AreEqual("bestaand", user.CondorMainFolder); Assert.AreEqual("Bewaard", user.ScenarioNames[1]); Assert.AreEqual(1, app.Groups.Count);
    }

    [TestMethod] public void GroepenHerstellenSorterenEnRasterBeperken()
    {
        var app = new AppSettings { Groups = [new ScenarioGroup { GroupId = "a", DisplayName = "A", SortOrder = 1 }] };
        var user = new UserSettings { GroupPreferences = [new GroupPreference { GroupId = "b", DisplayName = "B", SortOrder = 2 }, new GroupPreference { GroupId = "a", DisplayName = "A", SortOrder = 1 }] };
        Assert.AreEqual("A", ConfigurationMigration.SortedGroups(user)[0].DisplayName); Assert.AreEqual("A", ConfigurationMigration.RestoreDefaultGroups(app)[0].DisplayName); Assert.AreEqual(3, ConfigurationMigration.GridColumns(13));
    }

    [TestMethod] public void VersieOntbreektZonderFout()
    {
        Assert.AreEqual("onbekend", VersionService.CondorVersion(Path.Combine(_root, "niet-aanwezig.exe")));
    }

    [TestMethod] public void StatusmachineBlokkeertDubbelklikEnParallelleStart()
    {
        var machine = new FlightStateMachine(); Assert.IsTrue(machine.TryBegin()); Assert.IsFalse(machine.TryBegin());
        machine.MoveTo(FlightSessionState.Flying); Assert.IsFalse(machine.TryBegin()); machine.Reset(); Assert.IsTrue(machine.TryBegin());
    }

    [TestMethod] public void NormaleCyclusEnTweedeVluchtKunnenNaarReady()
    {
        var machine = new FlightStateMachine(); Assert.IsTrue(machine.TryBegin());
        foreach (var state in new[] { FlightSessionState.StartingCondor, FlightSessionState.OpeningFlightPlanner, FlightSessionState.StartingFlight, FlightSessionState.Flying, FlightSessionState.ClosingCondor, FlightSessionState.WaitingForExit }) machine.MoveTo(state);
        machine.Reset(); Assert.AreEqual(FlightSessionState.Ready, machine.State); Assert.IsTrue(machine.TryBegin());
    }

    [TestMethod] public void HandmatigSluitenCrashEnTimeoutKunnenNaarErrorOfReady()
    {
        var manual = new FlightStateMachine(); manual.TryBegin(); manual.MoveTo(FlightSessionState.Flying); manual.Reset(); Assert.AreEqual(FlightSessionState.Ready, manual.State);
        var crash = new FlightStateMachine(); crash.TryBegin(); crash.MoveTo(FlightSessionState.Error); Assert.AreEqual(FlightSessionState.Error, crash.State);
        var stuck = new FlightStateMachine(); stuck.TryBegin(); stuck.MoveTo(FlightSessionState.WaitingForExit); Assert.AreEqual(FlightSessionState.WaitingForExit, stuck.State);
    }
}
