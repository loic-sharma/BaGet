using System;
using System.Linq;
using Xunit;

namespace BaGet.Azure.Tests.Storage;

/// <summary>
/// Block ids are base64 (Azure requires it), and base64 text does not sort like the bytes it encodes: digits sort before letters
/// in ASCII but after them in the base64 alphabet. A 5 GB upload (more than ~320 blocks) was assembled out of order when the
/// staged blocks were ordered by id text, which only the 5 GB prototype of B2 caught.
/// </summary>
public class BlockIdOrderTests
{
    [Fact]
    public void BlocksAreOrderedByTheirIndexNotByTheTextOfTheirIds()
    {
        var indexes = Enumerable.Range(0, 3000).ToList();
        var ids = indexes.Select(BlobStorageService.BlockId).ToList();

        // The trap: ordinal order of the ids is not index order.
        var byText = ids.OrderBy(id => id, StringComparer.Ordinal).Select(BlobStorageService.BlockIndex).ToList();
        Assert.NotEqual(indexes, byText);

        var shuffled = ids.OrderBy(id => id.GetHashCode()).ToList();
        Assert.Equal(indexes, BlobStorageService.InUploadOrder(shuffled).Select(BlobStorageService.BlockIndex).ToList());
    }

    [Fact]
    public void IdsAreAllTheSameLengthAsAzureRequires()
    {
        Assert.Single(Enumerable.Range(0, 50000).Select(i => BlobStorageService.BlockId(i).Length).Distinct());
    }
}
