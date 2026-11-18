using System.Collections.Generic;
using NUnit.Framework;
using Ouroboros.Core;

namespace Ouroboros.Tests
{
    public class PatternRulesTests
    {
        [Test]
        public void EveryPixel_IsOneOfTheCrewsFourColours()
        {
            for (int p = 0; p < PatternRules.PatternCount; p++)
                for (int y = 0; y < PatternRules.Size; y++)
                    for (int x = 0; x < PatternRules.Size; x++)
                        Assert.That(PatternRules.Index(p, x, y), Is.InRange(0, PatternRules.ColourCount - 1));
        }

        [Test]
        public void Patterns_TileSeamlessly()
        {
            for (int p = 0; p < PatternRules.PatternCount; p++)
            {
                for (int i = 0; i < PatternRules.Size; i += 3)
                {
                    Assert.That(PatternRules.Index(p, i + PatternRules.Size, 17), Is.EqualTo(PatternRules.Index(p, i, 17)));
                    Assert.That(PatternRules.Index(p, 41, i - PatternRules.Size), Is.EqualTo(PatternRules.Index(p, 41, i)));
                }
            }
        }

        [Test]
        public void EveryPattern_UsesAtLeastThreeColours_AndNoColourDrownsTheRest()
        {
            for (int p = 0; p < PatternRules.PatternCount; p++)
            {
                var counts = new int[PatternRules.ColourCount];
                for (int y = 0; y < PatternRules.Size; y++)
                    for (int x = 0; x < PatternRules.Size; x++)
                        counts[PatternRules.Index(p, x, y)]++;

                int used = 0, total = PatternRules.Size * PatternRules.Size;
                foreach (int c in counts) if (c > total / 100) used++;
                Assert.That(used, Is.GreaterThanOrEqualTo(3), $"pattern {p} is nearly one flat colour");
                foreach (int c in counts) Assert.That(c, Is.LessThan(total * 8 / 10), $"pattern {p}: one colour covers over 80%");
            }
        }

        [Test]
        public void Patterns_AreDifferentFromEachOther()
        {
            for (int a = 0; a < PatternRules.PatternCount; a++)
            {
                for (int b = a + 1; b < PatternRules.PatternCount; b++)
                {
                    int differing = 0;
                    for (int y = 0; y < PatternRules.Size; y += 2)
                        for (int x = 0; x < PatternRules.Size; x += 2)
                            if (PatternRules.Index(a, x, y) != PatternRules.Index(b, x, y)) differing++;
                    Assert.That(differing, Is.GreaterThan(PatternRules.Size * PatternRules.Size / 16), $"patterns {a} and {b} look alike");
                }
            }
        }

        [Test]
        public void PatternIndex_WrapsSoABadIdNeverThrows()
        {
            Assert.That(PatternRules.Index(PatternRules.PatternCount + 1, 5, 5), Is.EqualTo(PatternRules.Index(1, 5, 5)));
            Assert.That(PatternRules.Index(-1, 5, 5), Is.EqualTo(PatternRules.Index(PatternRules.PatternCount - 1, 5, 5)));
        }

        [Test]
        public void EveryPattern_HasStructure_NotNoise()
        {
            // Neighbouring pixels mostly share a colour: these are shapes, not static.
            for (int p = 0; p < PatternRules.PatternCount; p++)
            {
                int same = 0, total = 0;
                for (int y = 0; y < PatternRules.Size; y++)
                {
                    for (int x = 0; x < PatternRules.Size; x++)
                    {
                        if (PatternRules.Index(p, x, y) == PatternRules.Index(p, x + 1, y)) same++;
                        total++;
                    }
                }
                Assert.That(same, Is.GreaterThan(total / 2), $"pattern {p} changes colour on most pixels");
            }
        }
    }
}
