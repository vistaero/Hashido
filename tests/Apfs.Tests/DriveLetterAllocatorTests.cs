using Apfs.Mount;

namespace Apfs.Tests;

public class DriveLetterAllocatorTests
{
    [Fact]
    public void AutomaticSelectionSkipsOccupiedLettersInCandidateRange()
    {
        // Simula ocupadas las letras de red habituales Z:/Y: y T: en el mapa que ve el servicio.
        uint occupied = (1u << ('Z' - 'A')) | (1u << ('Y' - 'A')) | (1u << ('T' - 'A'));

        Assert.Equal("S:", DriveLetterAllocator.FindFreeMountPoint(occupied));
    }

    [Fact]
    public void AutomaticSelectionUsesHighestFreeLetterAndSkipsFloppyLetters()
    {
        Assert.Equal("T:", DriveLetterAllocator.FindFreeMountPoint(0));

        uint occupied = (1u << ('B' - 'A')) | (1u << ('A' - 'A'));
        Assert.Equal("T:", DriveLetterAllocator.FindFreeMountPoint(occupied));
    }

    [Fact]
    public void AutomaticSelectionFailsClearlyWhenNoLetterIsFree()
    {
        uint occupied = 0;
        foreach (char letter in "TSRQPONMLKJIHGF")
            occupied |= 1u << (letter - 'A');

        var error = Assert.Throws<ApfsException>(() => DriveLetterAllocator.FindFreeMountPoint(occupied));

        Assert.Equal(ApfsErrorCode.IoError, error.Code);
    }
}
