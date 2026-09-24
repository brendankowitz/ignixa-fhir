using System.IO.Compression;
using System.Security.Cryptography;
using Shouldly;

namespace Ignixa.PackageManagement.Tests;

internal static class StrictWireFixture
{
    // Literal gzip vectors freeze headers, payloads and compression across runtime versions.
    // Their expanded tar uses fixed USTAR headers, Unix epoch times and two zero end blocks.
    internal static (byte[] Gzip, byte[] Tar) Load(string name)
    {
        (string encoded, string gzipHash, string tarHash) = name switch
        {
            "nul-checksum" => (
                "H4sIAAAAAAACCu2Uy2rDMBBF/SnlroU0fjQLfUUgpZuShepMbbexLSS5pJT+e/EjfWTTTRoI6GxGjDQIIe6xpnwxFSs7V/ns+y45M0REq6KYKhGdVkqz9Gs99dOcVpTcUHIBBh+MS4jO8cifj7sS3tGZlqHBB9PaPcununHSs3FlDYFXdr7pO2ikMpMEgXH/fu566AcUkmQKgVySzLD9SCLXxJJ75dj3gyv/RQB/5T+7LX7lfxRAnuUx/xfJ//Hn797s6IHNFP21cablwA4CzQ4agX2AwOD20KhDsF4rdXRG7yp1MqeWgbLf8ff4o/E8SmNtQsNdwFYgzNf64JquggAfrGO/SGc5JydHRbVEIpHIGfkENthMWAAMAAA=",
                "F3CE84E61FF4B0221DD77DD8346280FB3358D087E3DE47E7222AE8153A33DD91",
                "C761E0EEF1E70E410529D4F8713845BF835616015CE00D158330A5F2A96F992D"),
            "nul-uid" => (
                "H4sIAAAAAAACCu2U3UrEMBBG+yjyXYdkmq0L9ikWFG9kL2J37Fa3bUhSWRHfXfqn0uu6sJBzM2GSIYTwHWuKN1OysmOVr75tkpUhItpm2VCJaFkp1enPeuinG9pSckPJBeh8MC4hWuORfx93JXyiMTUjB59NbU8sX46Vk56NK44QeGfnq7ZBjlRqSRDo9x/Hrkf+hEySTCGwkSQ19l9J5JqYcq8c+7Zzxb8IYM5/cjeyzL++zZb5z7SO+b9M/ueff/iwvQfuh+jvjDM1B3YQqA7IEdgHCHTuhBzHEKzPlZqd0bpSLebUNFC0B/4dfzaee2nsTKi4CdgLhPFaH1zVlBDgs3XsJ+lM5+TgqKiWSCQSWZFvyvVROwAMAAA=",
                "0E9B89C8970FAD7FD17FFFD9F3E1174AD97A5CB3B8950200BD8FCC65D6704260",
                "81BA03ED4FF12E89C9B30625E31DEDC677FA6B116FCFC0E48516CF9BC5A07858"),
            "pax-newline-size" => (
                "H4sIAAAAAAACCu2WQWuDMBTHPfdTyDtLfImuBaH3HYWNXUYPqb7ZbDVKkg67se8+au02vPTSCmX5XV6IPkJ4/MmvlcWbrChuj5W92kYHFwYRcZ6mfUXEcUUu+M+63+cJzjEIMZiAnXXSBIiXuOTfy90In6BlTZABdbJut8ReNsowS9IUG4jgnYxVjYYMOBMMIYLD96fjroXsGVKGjEMECUMmYPUVeG6JXHb3JEsyNibtzP4aZ5zLPyZilH+RCgzCzuf/6oh5WDR1TdotuxlPQqs+aMlRpDOfjf/A8O7HqtKNoZKtlZ4+/+M1Fwtc+Pd/yvkbss3OFFcRwHPzF3fp2P8Skfj5T+N/p8k/7tuDBz706pdLI2tyZCACVUIGjqyDCHZmCxlsnGttFscnZ2xMFY/64qGhaEr6bV9LSwdpzKVTpB2sInDHY60zSlcQAXWtITtI5/Af6x3Vq6XH4/FckG9B1ONHABIAAA==",
                "49E5404D23FAECE1232084C81EB9D5879FE0794EF2139D90E2CBF444B5447C9C",
                "B8436447B414CDF4FB0B01ADA1E9FD8D36EBD8A39E8C7A4A006E1FD3C4591D29"),
            "trailing-space-path" => (
                "H4sIAAAAAAACCu2TwQqCQBCG91FkzjL+uy4efJAu0WGRLS012S0JonePNCK8dBFB2u/yD/8chjl8nSlO5mCTbkw++nMrZgYAMq2HBDBNSCU/89DLFBlEBLEAV38xTgBzPPn93Eq4U2saSznZm2m62vK+rBx7a1xRUky9db46t5STZMWgmF77zdh6yrekGSwpppTBinYPEVgTb+8T21f1IH80/41f/gNq4r/SmQr+L+J/EDYQCAT+kicMXieSAAwAAA==",
                "9681A5A1E209BFC4274E4ABDE47E73E3333037AA449E95A924DA08FD34F8E85B",
                "47C8092CCA8EC3E0EF8E105C4042047BFA77D4FF5E393C8D22FF513DC544BBF4"),
            "gnu-unterminated" => (
                "H4sIAAAAAAACCu2VwQrCMBBE8yllz2WdtEWh39CzF/EQJNZaTUujIoj/LlYR6UUPVinuu0zYHMKwTKY2i9LkdlTflNe+curDAMA4SVoF0FXoSD/O7VzHGEMFUF9g73emUcAnTD6bGwgncmZrKSV7NNt6Y3m5Khr21jSLFYV0sI0vKkcpaY4YFNL1fnqbekpnlDBYU0gxgyOan5UwJLLK5Vnhyj7feJX/ZNLNPyaIVZBJ/nvn/u+PjPA2vZTkr/ffo6lX+QeiTv51m3/p/y/0vxS2IAjCX3IB6ANXTgAQAAA=",
                "7A09583D41C710F0ACA96A6630DC30B655CAD6BC7BF493BC62E2D31B453DFEF2",
                "EE174A54F0153D889497E8186F3EA1344FAF54592B091E85592E1830BCFA6CA8"),
            "gnu-nul" => (
                "H4sIAAAAAAACCu2VzQrCMBCE8yhlz2WdtMFCn6FnL+IhSNT6k5ZGRRDfXbQi0oserCLud5mwOYRhmUxtpys7d4O6VV6Gyqs3AwBDY64KoKvQib6fr3OdYggVQX2AXdjaRgHvMPlo7kc4krcbRzm5g93Ua8ezRdlwcLaZLiimvWtCWXnKSXPCoJgu96N2Gigfk2GwpphSBic0OSnhlygqPy9Kv+rzjWf5N1nSyT8yGBUVkv/euf37Ayu8TC8l+e3992jqWf6Bbv51hlT6/yP9L4UtCILwl5wBi9jvnQAQAAA=",
                "18B6FD6BA77E13650788FE394C963707F6D938DFF20513FCFD484676B15C4A54",
                "C8099F40A399F20ED7282182CF518AFB78A58A31420D32C8E29F4C08296182BF"),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        byte[] gzip = Convert.FromBase64String(encoded);
        Hash(gzip).ShouldBe(gzipHash);
        using var input = new MemoryStream(gzip);
        using var reader = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        reader.CopyTo(output);
        byte[] tar = output.ToArray();
        Hash(tar).ShouldBe(tarHash);
        return (gzip, tar);
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
