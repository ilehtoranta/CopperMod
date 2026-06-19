using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CopperMod.Abstractions;

namespace CopperMod.Sid
{
    /// <summary>
    /// PSID/RSID SID music loader.
    /// </summary>
    public sealed class SidFormat : IModuleFormatWithContext
    {
        /// <inheritdoc />
        public string Name => "C64 SID";

        /// <inheritdoc />
        public bool CanLoad(ReadOnlySpan<byte> data)
        {
            return SidParser.Identify(data) || C64CartridgeParser.Identify(data) || IsZip(data);
        }

        /// <inheritdoc />
        public bool CanLoad(ModuleLoadContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return CanLoad(context.DataSpan) || IsPrg(context);
        }

        /// <inheritdoc />
        public IModuleSong Load(ReadOnlySpan<byte> data)
        {
            if (SidParser.Identify(data))
            {
                var copy = data.ToArray();
                var module = SidParser.Parse(copy);
                return new SidSong(module);
            }

            if (C64CartridgeParser.Identify(data))
            {
                var cartridge = C64CartridgeParser.Parse(data);
                return new SidSong(SidModule.CreateEasyFlashCartridge(cartridge));
            }

            if (IsZip(data))
            {
                var cartridgeData = ExtractSingleCrtFromZip(data);
                var cartridge = C64CartridgeParser.Parse(cartridgeData);
                return new SidSong(SidModule.CreateEasyFlashCartridge(cartridge));
            }

            throw new UnsupportedModuleFormatException("The data is not a supported PSID/RSID SID file or EasyFlash CRT cartridge.");
        }

        /// <inheritdoc />
        public IModuleSong Load(ModuleLoadContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (IsPrg(context))
            {
                var title = Path.GetFileNameWithoutExtension(context.SourcePath) ?? "C64 PRG";
                return new SidSong(SidModule.CreateBasicProgram(context.DataSpan, title));
            }

            return Load(context.DataSpan);
        }

        private static bool IsPrg(ModuleLoadContext context)
        {
            return context.DataSpan.Length >= 3 &&
                !string.IsNullOrWhiteSpace(context.SourcePath) &&
                string.Equals(Path.GetExtension(context.SourcePath), ".prg", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsZip(ReadOnlySpan<byte> data)
        {
            return data.Length >= 4 &&
                data[0] == 0x50 &&
                data[1] == 0x4B &&
                data[2] == 0x03 &&
                data[3] == 0x04;
        }

        private static byte[] ExtractSingleCrtFromZip(ReadOnlySpan<byte> data)
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name) && string.Equals(Path.GetExtension(entry.Name), ".crt", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (entries.Length != 1)
            {
                throw new ModuleLoadException("ZIP cartridge input must contain exactly one .crt file.");
            }

            using var entryStream = entries[0].Open();
            using var output = new MemoryStream();
            entryStream.CopyTo(output);
            return output.ToArray();
        }
    }
}
