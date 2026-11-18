using NUnit.Framework;
using Ouroboros.Core;

namespace Ouroboros.Rules.Tests
{
    public class DamageLogTests
    {
        [Test]
        public void Add_GroupsHitsBySourceAndMeans()
        {
            var log = new DamageLog();
            log.Add("Ada", "Assault Rifle", 14f);
            log.Add("Ada", "Assault Rifle", 14f);
            log.Add("Ada", "Service Pistol", 18f);
            log.Add("Guard", "Rifle", 10f);

            Assert.That(log.Count, Is.EqualTo(3));
            Assert.That(log.Total, Is.EqualTo(56f));
            var top = log.Sorted()[0];
            Assert.That(top.Source, Is.EqualTo("Ada"));
            Assert.That(top.Means, Is.EqualTo("Assault Rifle"));
            Assert.That(top.Total, Is.EqualTo(28f));
            Assert.That(top.Hits, Is.EqualTo(2));
        }

        [Test]
        public void Add_IgnoresNothingAndNegativeDamage()
        {
            var log = new DamageLog();
            log.Add("Ada", "Rifle", 0f);
            log.Add("Ada", "Rifle", -5f);
            Assert.That(log.Count, Is.EqualTo(0));
            Assert.That(log.Total, Is.EqualTo(0f));
        }

        [Test]
        public void Add_FoldsTheSmallestSourcesWhenFull_AndKeepsTheTotal()
        {
            var log = new DamageLog();
            for (int i = 0; i < 12; i++) log.Add("Source " + i, "", 10f + i);
            Assert.That(log.Count, Is.LessThanOrEqualTo(DamageLog.MaxEntries));

            float sum = 0f;
            bool hasOther = false;
            foreach (var e in log.Entries) { sum += e.Total; hasOther |= e.Source == DamageLog.OtherSource; }
            Assert.That(sum, Is.EqualTo(log.Total).Within(1e-3f), "folding must not lose damage");
            Assert.That(hasOther, Is.True);
            Assert.That(log.Sorted()[0].Source, Is.Not.EqualTo("Source 0"), "the biggest sources survive");
        }

        [Test]
        public void Clear_StartsANewLife()
        {
            var log = new DamageLog();
            log.Add("Ada", "Rifle", 30f);
            log.Clear();
            Assert.That(log.Count, Is.EqualTo(0));
            Assert.That(log.Total, Is.EqualTo(0f));
        }

        [Test]
        public void EncodeThenDecode_RoundTrips()
        {
            var log = new DamageLog();
            log.Add("Ada", "Marksman Rifle", 90f);
            log.Add("Sentry Turret", "Security", 24.4f);
            log.Add("Fire", "Fire", 6f);

            Assert.That(DamageLog.TryDecode(log.Encode("Ada", "Marksman Rifle"), out var report), Is.True);
            Assert.That(report.Killer, Is.EqualTo("Ada"));
            Assert.That(report.KillerMeans, Is.EqualTo("Marksman Rifle"));
            Assert.That(report.Entries.Count, Is.EqualTo(3));
            Assert.That(report.Entries[0].Source, Is.EqualTo("Ada"), "largest first");
            Assert.That(report.Entries[0].Total, Is.EqualTo(90f));
            Assert.That(report.Entries[1].Total, Is.EqualTo(24f), "rounded to whole points");
            Assert.That(report.Total, Is.EqualTo(120f));
        }

        [Test]
        public void Encode_CannotBeBrokenByALabel()
        {
            var log = new DamageLog();
            log.Add("Ev|il\nName", "Gun|\n", 50f);
            Assert.That(DamageLog.TryDecode(log.Encode("Ev|il\nName", "Gun|\n"), out var report), Is.True);
            Assert.That(report.Entries.Count, Is.EqualTo(1));
            Assert.That(report.Killer, Is.EqualTo("EvilName"));
            Assert.That(report.Entries[0].Means, Is.EqualTo("Gun"));
        }

        [Test]
        public void Encode_StaysSmallEnoughForOneMessage()
        {
            var log = new DamageLog();
            for (int i = 0; i < 40; i++) log.Add(new string((char)('A' + i % 26), 60), new string('z', 60), 5f + i);
            string encoded = log.Encode(new string('K', 60), new string('m', 60));
            Assert.That(encoded.Length, Is.LessThan(420));
        }

        [TestCase(null)]
        [TestCase("")]
        public void TryDecode_RejectsEmptyInput(string encoded)
        {
            Assert.That(DamageLog.TryDecode(encoded, out _), Is.False);
        }

        [Test]
        public void TryDecode_SkipsBrokenLines()
        {
            Assert.That(DamageLog.TryDecode("Ada|Rifle\ngarbage\nGuard|Rifle|x|2\nFire|Fire|12|3", out var report), Is.True);
            Assert.That(report.Entries.Count, Is.EqualTo(1));
            Assert.That(report.Entries[0].Source, Is.EqualTo("Fire"));
        }
    }
}
