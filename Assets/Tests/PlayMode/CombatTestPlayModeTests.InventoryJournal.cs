using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_QueuedWeaponCarryJournalKeepsItsSelectionRequests()
        {
            string folder = Path.GetFullPath("TestResults/Test combat inventory journal");
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(CombatTestStartService.TryStart(new CombatLoadoutSelection(CombatWeaponId.Pistol)), Is.True);
                yield return AwaitSelectedCombatRange();
                PlacePair(8f);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                root.SendMessage("OnApplicationFocus", true); GameInput.HandleApplicationFocus(true);
                yield return null;
                root.SetDuelLogging(true, folder);
                CombatActor hero = root.Hero;
                Assert.That(hero.TrySelectWeapon(CombatWeaponId.Shotgun), Is.True);
                int firstRequest = hero.JournalRequestId;
                Assert.That(hero.IsSwitchingWeapon, Is.True);
                Assert.That(hero.WeaponCarryActionWeapon, Is.EqualTo(CombatWeaponId.Pistol));
                Assert.That(hero.WeaponCarryActionIsDraw, Is.False);
                Assert.That(hero.TrySelectWeapon(CombatWeaponId.Crowbar), Is.True);
                int replacementRequest = hero.JournalRequestId;
                Assert.That(replacementRequest, Is.GreaterThan(firstRequest));

                // The existing handoff proof also submits rejected attack/fire
                // requests on each carry frame. Those newer IDs must never own
                // the accepted stow or the queued replacement's draw phase.
                yield return FinishWeaponCarry(CombatWeaponId.Crowbar, null);
                Assert.That(hero.JournalRequestId, Is.GreaterThan(replacementRequest));
                DuelJournal journal = root.JournalForDiagnostics;
                string path = Path.Combine(folder, "duel_" + journal.SessionId + "_" +
                    journal.Round.ToString("D6", CultureInfo.InvariantCulture), "duel.ndjson");
                // A flush barrier covers preceding packets, not future Update
                // snapshots. End this owned capture before parsing its complete
                // file, so the reader cannot observe a newly appended half-line.
                root.SetDuelLogging(false);
                var flush = journal.FlushAsync();
                yield return WaitFor(() => flush.IsCompleted, "Test carry journal did not flush.");
                Assert.That(flush.IsFaulted, Is.False);
                var stow = new HashSet<string>();
                var draw = new HashSet<string>();
                bool laterRejectedCommand = false;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        CarryInventoryJournalRecord record = JsonUtility.FromJson<CarryInventoryJournalRecord>(line);
                        if (record.data == null || record.data.actor != hero.JournalActorId) continue;
                        laterRejectedCommand |= record.@event == "command_result" && record.data.result == "rejected" &&
                            record.data.request > replacementRequest;
                        if (record.@event != "weapon_carry" || record.data.phase == "request") continue;
                        Assert.That(record.data.weapon, Is.Not.EqualTo("Shotgun"), "The superseded draw must not start.");
                        if (record.data.weapon == "Pistol")
                        {
                            Assert.That(record.data.request, Is.EqualTo(firstRequest), record.data.phase);
                            Assert.That(record.data.drawing, Is.False);
                            stow.Add(record.data.phase);
                        }
                        else if (record.data.weapon == "Crowbar")
                        {
                            Assert.That(record.data.request, Is.EqualTo(replacementRequest), record.data.phase);
                            Assert.That(record.data.drawing, Is.True);
                            draw.Add(record.data.phase);
                        }
                    }
                }
                Assert.That(stow, Is.EquivalentTo(new[] { "begin", "transfer", "complete" }));
                Assert.That(draw, Is.EquivalentTo(new[] { "begin", "transfer", "complete" }));
                Assert.That(laterRejectedCommand, Is.True, "The proof must include intervening rejected commands.");
                LogAssert.NoUnexpectedReceived();
            }
            finally { if (root != null) root.SetDuelLogging(false); }
        }

        [Serializable]
        private sealed class CarryInventoryJournalRecord
        {
            public string @event;
            public CarryInventoryJournalData data;
        }

        [Serializable]
        private sealed class CarryInventoryJournalData
        {
            public int actor, request;
            public string phase, weapon, result;
            public bool drawing;
        }
    }
}
