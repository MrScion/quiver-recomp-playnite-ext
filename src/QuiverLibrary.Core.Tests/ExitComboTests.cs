using QuiverLibrary.Core.Gamepad;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class ExitComboTests
    {
        private const ushort Back = ExitCombo.Back;
        private const ushort Start = ExitCombo.Start;

        [Fact]
        public void BackHeldThenStartTriggersOnce()
        {
            var combo = new ExitCombo();
            Assert.False(combo.Update(0, 0));
            Assert.False(combo.Update(0, Back));
            Assert.True(combo.Update(0, Back | Start));
            Assert.False(combo.Update(0, Back | Start));
        }

        [Fact]
        public void StartFirstDoesNotCount()
        {
            var combo = new ExitCombo();
            Assert.False(combo.Update(0, Start));
            Assert.False(combo.Update(0, Start | Back));
            Assert.False(combo.Update(0, Start | Back));
        }

        [Fact]
        public void BothInTheSamePollDoNotCount()
        {
            var combo = new ExitCombo();
            Assert.False(combo.Update(0, 0));
            Assert.False(combo.Update(0, Back | Start));
        }

        [Fact]
        public void BackReleasedBeforeStartDoesNotCount()
        {
            var combo = new ExitCombo();
            Assert.False(combo.Update(0, Back));
            Assert.False(combo.Update(0, 0));
            Assert.False(combo.Update(0, Start));
        }

        [Fact]
        public void PadsAreSeparate()
        {
            var combo = new ExitCombo();
            Assert.False(combo.Update(0, Back));
            Assert.False(combo.Update(1, Start));
            Assert.False(combo.Update(1, 0));
            Assert.True(combo.Update(0, Back | Start));
        }

        [Fact]
        public void ResetForgetsHeldBack()
        {
            var combo = new ExitCombo();
            combo.Update(2, Back);
            combo.Reset(2);
            Assert.False(combo.Update(2, Back | Start));
        }

        [Fact]
        public void OtherButtonsDoNotMatter()
        {
            var combo = new ExitCombo();
            Assert.False(combo.Update(3, (ushort)(Back | 0x1000)));
            Assert.True(combo.Update(3, (ushort)(Back | Start | 0x2000)));
        }

        [Fact]
        public void ProgramsInsideTheGameFolders()
        {
            var folders = GameProcessFilter.Folders(new[] { @"E:\Quiver\Apps\Game", @"E:\Quiver\games\Game\play\", null, "", @"E:\Quiver\Apps\Game\" });
            Assert.Equal(2, folders.Count);
            Assert.True(GameProcessFilter.IsInside(@"E:\Quiver\Apps\Game\bin\game.exe", folders));
            Assert.True(GameProcessFilter.IsInside(@"e:\quiver\games\game\play\hwder.exe", folders));
            Assert.False(GameProcessFilter.IsInside(@"E:\Quiver\Apps\GameTwo\game.exe", folders));
            Assert.False(GameProcessFilter.IsInside(@"C:\Windows\explorer.exe", folders));
            Assert.False(GameProcessFilter.IsInside(null, folders));
        }

        [Fact]
        public void DriveRootsAreLeftOut()
        {
            Assert.Empty(GameProcessFilter.Folders(new[] { @"C:\", "C:" + "\\\\" }));
        }
    }
}
