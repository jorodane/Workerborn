using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class InteractionPresentationTests
    {
        [Test]
        public void PlainNamesKeepTheirOriginalPositionsIncludingViewportEdges()
        {
            var labels = new List<NameplateSlot>
            {
                new NameplateSlot
                {
                    Order = 2,
                    X = -20,
                    Y = -5,
                    Width = 80,
                    Height = 24
                },
                new NameplateSlot
                {
                    Order = 1,
                    X = -20,
                    Y = -5,
                    Width = 80,
                    Height = 24
                }
            };
            NameplateLayout.Present(labels, false, 0, 100, 3);
            Assert.That(labels.TrueForAll(l => l.PlacedY == -5 && l.X == -20), Is.True);
            NameplateLayout.Present(labels, true, 0, 100, 3);
            Assert.That(labels[0].Order, Is.EqualTo(1));
            Assert.That(labels[0].PlacedY, Is.GreaterThan(labels[1].PlacedY));
            NameplateLayout.Present(labels, false, 0, 100, 3);
            Assert.That(labels.TrueForAll(l => l.PlacedY == -5 && l.X == -20), Is.True);
        }

        [Test]
        public void EqualScreenHeightsStackInCreationOrderAndKeepTheirHorizontalPositions()
        {
            var slots = new List<NameplateSlot>();
            foreach (int order in new[]
            {
                4,
                2,
                5,
                1,
                3
            }

            )
                slots.Add(new NameplateSlot { Order = order, X = 100, Y = 150, Width = 80, Height = 24 });
            NameplateLayout.Arrange(slots, 0, 300, 3);
            Assert.That(slots.Select(s => s.Order), Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }));
            for (int i = 0; i < slots.Count; i++)
            {
                Assert.That(slots[i].X, Is.EqualTo(100));
                Assert.That(slots[i].PlacedY, Is.InRange(12, 288));
                if (i > 0)
                    Assert.That(slots[i - 1].PlacedY - slots[i].PlacedY, Is.GreaterThanOrEqualTo(27));
            }
        }

        [Test]
        public void ScreenHeightTakesPriorityOverCreationOrderAndSeparateColumnsStayStill()
        {
            var slots = new List<NameplateSlot>
            {
                new NameplateSlot
                {
                    Order = 1,
                    X = 50,
                    Y = 100,
                    Width = 70,
                    Height = 24
                },
                new NameplateSlot
                {
                    Order = 2,
                    X = 50,
                    Y = 120,
                    Width = 70,
                    Height = 24
                },
                new NameplateSlot
                {
                    Order = 3,
                    X = 250,
                    Y = 120,
                    Width = 70,
                    Height = 24
                }
            };
            NameplateLayout.Arrange(slots, 0, 300, 3);
            Assert.That(slots.Find(s => s.Order == 2).PlacedY, Is.GreaterThan(slots.Find(s => s.Order == 1).PlacedY));
            Assert.That(slots.Find(s => s.Order == 3).PlacedY, Is.EqualTo(120));
        }

        [Test]
        public void OverlapUsesObjectHeightAndLeavesDistantLabelsInTheSameColumnAlone()
        {
            var crop = new NameplateSlot { Order = 2, X = 50, Y = 100, ObjectY = 90, Width = 60, Height = 24 };
            var npc = new NameplateSlot { Order = 1, X = 50, Y = 114, ObjectY = 70, Width = 60, Height = 24 };
            var distant = new NameplateSlot { Order = 3, X = 50, Y = 206, ObjectY = 200, Width = 60, Height = 24 };
            var slots = new List<NameplateSlot> { npc, distant, crop };
            NameplateLayout.Arrange(slots, 0, 300, 3);
            Assert.That(crop.PlacedY - npc.PlacedY, Is.GreaterThanOrEqualTo(27));
            Assert.That(distant.PlacedY, Is.EqualTo(distant.Y));
        }

        [Test]
        public void AStackFitsAroundAnUnobstructedNameWithoutMovingIt()
        {
            var a = new NameplateSlot { Order = 1, X = 50, Y = 90, Width = 60, Height = 20 };
            var b = new NameplateSlot { Order = 2, X = 50, Y = 90, Width = 60, Height = 20 };
            var c = new NameplateSlot { Order = 3, X = 50, Y = 55, Width = 60, Height = 20 };
            var slots = new List<NameplateSlot> { a, b, c };
            NameplateLayout.Arrange(slots, 0, 180, 5);
            Assert.That(a.PlacedY - b.PlacedY, Is.GreaterThanOrEqualTo(25));
            Assert.That(b.PlacedY - c.PlacedY, Is.GreaterThanOrEqualTo(25));
            Assert.That(c.PlacedY, Is.EqualTo(c.Y));
        }

        [Test]
        public void AProjectedCropGridUsesFixedSizeBoxes()
        {
            var slots = new List<NameplateSlot>();
            for (int row = 0; row < 6; row++)
                for (int column = 0; column < 4; column++)
                    slots.Add(new NameplateSlot
                    {
                        Order = slots.Count,
                        X = 100 + column * 66 + row * 6,
                        Y = 330 - row * 37 - column * 4,
                        Width = 78,
                        Height = 46
                    });
            NameplateLayout.Arrange(slots, 0, 400, 3, left: 0, right: 460);

            Assert.That(slots.All(s => s.Width == 78f && s.Height == 46f), Is.True);
            AssertSeparated(slots, 3f);
            foreach (var slot in slots)
            {
                Assert.That(Math.Abs(slot.PlacedX - slot.X), Is.LessThanOrEqualTo(slot.Width * 1.5f + .001f));
                Assert.That(Math.Abs(slot.PlacedY - slot.Y), Is.LessThanOrEqualTo((slot.Height + 3f) * 2f + .001f));
            }
            NameplateLayout.Present(slots, false, 0, 400, 3);
            Assert.That(slots.All(s => s.PlacedX == s.X && s.PlacedY == s.Y), Is.True);
        }

        [Test]
        public void CrowdedFieldLabelsStayNearTheirProjectedPositions()
        {
            var slots = Enumerable.Range(0, 18).Select(index => new NameplateSlot
            {
                Order = index,
                X = 200,
                Y = 420 - index * 10,
                ObjectY = 400 - index * 10,
                Width = 64,
                Height = 26
            }).ToList();
            var sign = new NameplateSlot { Order = 18, X = 200, Y = 424, ObjectY = 380, Width = 112, Height = 26 };
            var unrelated = new NameplateSlot { Order = 19, X = 200, Y = 115, ObjectY = 105, Width = 70, Height = 26 };
            slots.Add(sign);
            slots.Add(unrelated);

            NameplateLayout.Arrange(slots, 0, 540, 3);
            Assert.That(unrelated.PlacedX, Is.EqualTo(unrelated.X));
            Assert.That(unrelated.PlacedY, Is.EqualTo(unrelated.Y));
            foreach (var label in slots)
            {
                Assert.That(Math.Abs(label.PlacedX - label.X), Is.LessThanOrEqualTo(label.Width * 1.5f + .001f));
                Assert.That(Math.Abs(label.PlacedY - label.Y), Is.LessThanOrEqualTo((label.Height + 3f) * 2f + .001f));
            }
        }

        [TestCase(0)]
        [TestCase(-85)]
        public void PackedCropsAndWideSignsUseOpenSpaceWithoutCoveringEachOther(int xOffset)
        {
            var slots = new List<NameplateSlot>();
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                {
                    float y = 333f - row * 14f - column * 6f;
                    Add(156f + column * 22f + row * 12f, y, y - 8f, 64f);
                }

            Add(164f, 340f, 314f, 184f);
            Add(238f, 320f, 294f, 150f);
            Add(140f, 299f, 276f, 164f);
            Add(295f, 353f, 329f, 108f);
            Add(300f, 316f, 283f, 96f, 47f);
            Add(94f, 345f, 323f, 116f);
            Add(590f, 280f, 250f, 100f);
            var separateSign = slots[slots.Count - 1];

            NameplateLayout.Arrange(slots, 40f, 560f, 3f, left: 8f, right: 792f);

            AssertSeparated(slots, 3f);
            Assert.That(separateSign.PlacedX, Is.EqualTo(separateSign.X));
            Assert.That(separateSign.PlacedY, Is.EqualTo(separateSign.Y));
            foreach (var slot in slots)
            {
                Assert.That(slot.PlacedX, Is.InRange(8f + slot.Width * .5f, 792f - slot.Width * .5f));
                Assert.That(slot.PlacedY, Is.InRange(40f + slot.Height * .5f, 560f - slot.Height * .5f));
            }

            void Add(float x, float y, float groundY, float width, float height = 40f) =>
                slots.Add(new NameplateSlot
                {
                    Order = slots.Count,
                    X = x + xOffset,
                    Y = y,
                    ObjectY = groundY,
                    Width = width,
                    Height = height
                });
        }

        [Test]
        public void RemovingACollectedItemClosesTheGapAndEdgeStacksStayInsideTheScreen()
        {
            var slots = new List<NameplateSlot>
            {
                new NameplateSlot
                {
                    Order = 1,
                    X = 50,
                    Y = 2,
                    Width = 60,
                    Height = 24
                },
                new NameplateSlot
                {
                    Order = 2,
                    X = 50,
                    Y = 2,
                    Width = 60,
                    Height = 24
                }
            };
            NameplateLayout.Arrange(slots, 0, 100, 4);
            Assert.That(slots.All(s => s.PlacedY >= 12 && s.PlacedY <= 88), Is.True);
            slots.RemoveAt(0);
            NameplateLayout.Arrange(slots, 0, 100, 4);
            Assert.That(slots[0].PlacedY, Is.EqualTo(12));
        }

        [Test]
        public void AStackThatIsTooTallUsesNearbyColumnsWithoutDroppingLabels()
        {
            var slots = Enumerable.Range(0, 9).Select(i =>
                new NameplateSlot { Order = i, X = 190, Y = 50, Width = 80, Height = 24 }).ToList();
            NameplateLayout.Arrange(slots, 0, 100, 4, left: 0, right: 380);
            AssertSeparated(slots, 4f);
            Assert.That(slots.All(s => s.Height == 24f && s.Width == 80f), Is.True);
            Assert.That(slots.All(s => s.PlacedX >= 40f && s.PlacedX <= 340f && s.PlacedY >= 12f && s.PlacedY <= 88f), Is.True);
            var positions = slots.Select(s => (s.PlacedX, s.PlacedY)).ToArray();
            slots.Reverse();
            NameplateLayout.Arrange(slots, 0, 100, 4, left: 0, right: 380);
            Assert.That(slots.Select(s => (s.PlacedX, s.PlacedY)), Is.EqualTo(positions));
        }

        [Test]
        public void AStackSpreadsWithoutCoveringAnUnobstructedName()
        {
            var slots = Enumerable.Range(1, 8).Select(i =>
                new NameplateSlot { Order = i, X = 150, Y = 42, Width = 60, Height = 20 }).ToList();
            var fixedName = new NameplateSlot { Order = 9, X = 150, Y = 85, Width = 80, Height = 20 };
            slots.Add(fixedName);
            NameplateLayout.Arrange(slots, 0, 100, 4, left: 0, right: 300);
            Assert.That(fixedName.PlacedX, Is.EqualTo(fixedName.X));
            Assert.That(fixedName.PlacedY, Is.EqualTo(fixedName.Y));
            AssertSeparated(slots, 4f);
        }

        private static void AssertSeparated(List<NameplateSlot> slots, float gap)
        {
            for (int i = 1; i < slots.Count; i++)
                for (int j = 0; j < i; j++)
                {
                    var first = slots[i];
                    var second = slots[j];
                    Assert.That(Math.Abs(first.PlacedX - second.PlacedX) >= (first.Width + second.Width) * .5f + gap - .001f ||
                        Math.Abs(first.PlacedY - second.PlacedY) >= (first.Height + second.Height) * .5f + gap - .001f,
                        Is.True, $"Labels {first.Order} and {second.Order} overlap.");
                }
        }

        [Test]
        public void TargetLineEndsOnAConcaveSilhouetteRatherThanItsBoundingBox()
        {
            var mask = new byte[6 * 6 * 4];
            // L-shaped target; the cursor sits in the missing upper-right corner of its bounds.
            for (int y = 1; y <= 4; y++)
                Paint(mask, 6, 1, y, 1);
            for (int x = 1; x <= 4; x++)
                Paint(mask, 6, x, 1, 1);
            Assert.That(TargetMaskGeometry.Closest(mask, 6, 6, false, 45, 35, 60, 60, out var hit), Is.True);
            Assert.That(hit.Id, Is.EqualTo(1));
            Assert.That(hit.X, Is.EqualTo(45));
            Assert.That(hit.Y, Is.EqualTo(20));
        }

        [Test]
        public void TargetUnderThePointerWinsEvenWhenABoundaryTieFavorsAnotherId()
        {
            var mask = new byte[8 * 8 * 4];
            for (int y = 1; y < 7; y++)
                for (int x = 1; x < 6; x++)
                    Paint(mask, 8, x, y, 9);
            Paint(mask, 8, 6, 3, 1);
            Assert.That(TargetMaskGeometry.Closest(mask, 8, 8, false, 35, 35, 80, 80, out var hit), Is.True);
            Assert.That(hit.Id, Is.EqualTo(9));
            Assert.That(hit.DistanceSquared, Is.EqualTo(625));
        }

        [Test]
        public void EmptyOrInvalidMasksDoNotLeaveAStaleSelection()
        {
            Assert.That(TargetMaskGeometry.Closest(new byte[16], 2, 2, false, 1, 1, 100, 100, out _), Is.False);
            Assert.That(TargetMaskGeometry.Closest(new byte[4], 2, 2, false, 1, 1, 100, 100, out _), Is.False);
        }

        [Test]
        public void DirectPickingUsesOnlyPaintedPixelsAndCtrlCanSkipProps()
        {
            var mask = new byte[8 * 8 * 4];
            Paint(mask, 8, 2, 2, 1);
            Paint(mask, 8, 6, 2, 8);
            Assert.That(TargetMaskGeometry.IdAt(mask, 8, 8, 2, 2, false), Is.EqualTo(1));
            Assert.That(TargetMaskGeometry.IdAt(mask, 8, 8, 3, 2, false), Is.Zero);
            Assert.That(TargetMaskGeometry.Closest(mask, 8, 8, false, 25, 25, 80, 80, out var hit, id => id == 8), Is.True);
            Assert.That(hit.Id, Is.EqualTo(8));
        }

        [Test]
        public void PointerCoordinatesUseThePublishedMaskOrientation()
        {
            var mask = new byte[4 * 3 * 4];
            Paint(mask, 4, 1, 0, 3);
            Paint(mask, 4, 1, 2, 7);
            Assert.That(TargetMaskGeometry.IdAtScreen(mask, 4, 3, true, 150, 20, 400, 300), Is.EqualTo(7));
            Assert.That(TargetMaskGeometry.IdAtScreen(mask, 4, 3, false, 150, 20, 400, 300), Is.EqualTo(3));
            Assert.That(TargetMaskGeometry.IdAtScreen(mask, 4, 3, true, -1, 20, 400, 300), Is.Zero);
        }

        [Test]
        public void ReadbackOrientationAndViewportScalingProduceTheSameEdge()
        {
            var bottom = new byte[4 * 4 * 4];
            Paint(bottom, 4, 1, 0, 3);
            var top = new byte[bottom.Length];
            Array.Copy(bottom, 0, top, 3 * 4 * 4, 4 * 4);
            TargetMaskGeometry.Closest(bottom, 4, 4, false, 10, 40, 40, 40, out var a);
            TargetMaskGeometry.Closest(top, 4, 4, true, 10, 40, 40, 40, out var b);
            Assert.That(a.Id, Is.EqualTo(b.Id));
            Assert.That(a.X, Is.EqualTo(b.X));
            Assert.That(a.Y, Is.EqualTo(b.Y));
        }

        [Test]
        public void MultiItemPurchasesAndSalesAreAtomicWithPhysicalCoins()
        {
            var state = VillageState.Restore(new VillageSave { gold = 50 });
            Assert.That(state.TryBuy(ItemIds.Bread, 4), Is.True);
            Assert.That(state.Gold, Is.EqualTo(10));
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.EqualTo(4));
            Assert.That(state.TryBuy(ItemIds.Bread, 2), Is.False);
            Assert.That(state.Gold, Is.EqualTo(10));
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.EqualTo(4));
            Assert.That(state.TrySellItems(ItemIds.Bread, 3), Is.True);
            Assert.That(state.Gold, Is.EqualTo(40));
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.EqualTo(1));
        }

        private static void Paint(byte[] bytes, int width, int x, int y, int id)
        {
            int index = (y * width + x) * 4;
            bytes[index] = (byte)id;
            bytes[index + 3] = 255;
        }
    }
}
