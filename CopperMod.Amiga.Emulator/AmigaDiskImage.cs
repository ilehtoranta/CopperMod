using System;
using System.IO;
using System.Runtime.InteropServices;
using CopperDisk;
using CopperDiskEncodedTrack = CopperDisk.AmigaEncodedTrack;
using CopperDiskTrackFeatures = CopperDisk.AmigaTrackFeatures;
using CoreEncodedTrack = CopperMod.Amiga.AmigaEncodedTrack;
using CoreTrackFeatures = CopperMod.Amiga.AmigaTrackFeatures;

namespace CopperMod.Amiga
{
    internal sealed class AmigaDiskImage : IAmigaDiskImage, IAmigaSectorDiskMedia
    {
        public const int SectorSize = AmigaDiskGeometry.SectorSize;
        public const int SectorsPerTrack = AmigaDiskGeometry.SectorsPerTrack;
        public const int HeadCount = AmigaDiskGeometry.HeadCount;
        public const int CylinderCount = AmigaDiskGeometry.CylinderCount;
        public const int StandardAdfSize = AmigaDiskGeometry.StandardAdfSize;
        internal const int TrackCount = AmigaDiskGeometry.TrackCount;

        private readonly IAmigaDiskMedia _media;
        private readonly IAmigaSectorDiskMedia? _sectorMedia;
        private readonly IWritableAmigaDiskMedia? _writableMedia;
        private readonly bool _hasPreservedTrackData;
        private readonly bool _defaultWriteProtected;
        private readonly string _name;
        private byte[]? _sectorData;

        private AmigaDiskImage(IAmigaDiskMedia media, string name, bool hasPreservedTrackData, bool defaultWriteProtected)
        {
            _media = media ?? throw new ArgumentNullException(nameof(media));
            _sectorMedia = media as IAmigaSectorDiskMedia;
            _writableMedia = media as IWritableAmigaDiskMedia;
            _name = name;
            _hasPreservedTrackData = hasPreservedTrackData;
            _defaultWriteProtected = defaultWriteProtected;
        }

        public byte[] Data
        {
            get
            {
                var data = RequireSectorMedia().SectorData;
                if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment) &&
                    segment.Offset == 0 &&
                    segment.Count == segment.Array!.Length)
                {
                    return segment.Array;
                }

                return _sectorData ??= data.ToArray();
            }
        }

        public string Name => _name;

        public bool HasCompleteSectorData => _sectorMedia?.HasCompleteDecodedSectorData == true;

        public bool HasPreservedTrackData => _hasPreservedTrackData;

        public bool DefaultWriteProtected => _defaultWriteProtected;

        public bool IsDirty => _writableMedia?.IsDirty == true;

        public bool CanWriteTracks => _writableMedia != null;

        public ReadOnlySpan<byte> BootBlock => RequireSectorMedia().BootBlock.Span;

        public static AmigaDiskImage Load(string path)
        {
            return WrapDiskException(() =>
            {
                var result = AmigaDiskLoader.Load(path);
                return new AmigaDiskImage(
                    result.Media,
                    result.DisplayName,
                    HasPreservedExtension(result.DisplayName),
                    GetDefaultWriteProtected(path, result.DisplayName));
            });
        }

        public static AmigaDiskImage FromAdfBytes(byte[] data, string name = "disk.adf")
        {
            return WrapDiskException(() => new AmigaDiskImage(
                AmigaDiskLoader.FromAdfBytes(data ?? throw new ArgumentNullException(nameof(data))),
                name,
                hasPreservedTrackData: false,
                defaultWriteProtected: true));
        }

        public static AmigaDiskImage FromEncodedTracks(byte[][] encodedTracks, string name = "disk.ipf")
        {
            return WrapDiskException(() => new AmigaDiskImage(
                AmigaDiskLoader.FromEncodedTrackBytes(encodedTracks),
                name,
                hasPreservedTrackData: true,
                defaultWriteProtected: true));
        }

        public static AmigaDiskImage FromEncodedTracks(CoreEncodedTrack[] encodedTracks, string name = "disk.ipf")
        {
            ArgumentNullException.ThrowIfNull(encodedTracks);
            var copperDiskTracks = new CopperDiskEncodedTrack[encodedTracks.Length];
            for (var i = 0; i < encodedTracks.Length; i++)
            {
                copperDiskTracks[i] = ToCopperDiskTrack(encodedTracks[i]);
            }

            return WrapDiskException(() => new AmigaDiskImage(
                AmigaDiskLoader.FromEncodedTracks(copperDiskTracks),
                name,
                hasPreservedTrackData: true,
                defaultWriteProtected: true));
        }

        public ReadOnlySpan<byte> ReadSector(int cylinder, int head, int sector)
        {
            return RequireSectorMedia().ReadSector(cylinder, head, sector).Span;
        }

        public ReadOnlySpan<byte> ReadSector(int logicalSector)
        {
            return RequireSectorMedia().ReadSector(logicalSector).Span;
        }

        public ReadOnlySpan<byte> ReadBytes(int byteOffset, int byteCount)
        {
            return RequireSectorMedia().ReadBytes(byteOffset, byteCount).Span;
        }

        public CoreEncodedTrack ReadEncodedTrack(int cylinder, int head)
        {
            return ToCoreTrack(_media.ReadTrack(cylinder, head));
        }

        public bool TryWriteEncodedTrack(int cylinder, int head, CoreEncodedTrack track)
        {
            if (_writableMedia == null)
            {
                return false;
            }

            var written = _writableMedia.TryWriteTrack(cylinder, head, ToCopperDiskTrack(track));
            if (written)
            {
                _sectorData = null;
            }

            return written;
        }

        int IAmigaDiskMedia.Cylinders => _media.Cylinders;

        int IAmigaDiskMedia.Heads => _media.Heads;

        IAmigaTrack IAmigaDiskMedia.ReadTrack(int cylinder, int head)
            => _media.ReadTrack(cylinder, head);

        bool IAmigaSectorDiskMedia.HasCompleteDecodedSectorData => HasCompleteSectorData;

        ReadOnlyMemory<byte> IAmigaSectorDiskMedia.SectorData => RequireSectorMedia().SectorData;

        ReadOnlyMemory<byte> IAmigaSectorDiskMedia.BootBlock => RequireSectorMedia().BootBlock;

        ReadOnlyMemory<byte> IAmigaSectorDiskMedia.ReadSector(int cylinder, int head, int sector)
            => RequireSectorMedia().ReadSector(cylinder, head, sector);

        ReadOnlyMemory<byte> IAmigaSectorDiskMedia.ReadSector(int logicalSector)
            => RequireSectorMedia().ReadSector(logicalSector);

        ReadOnlyMemory<byte> IAmigaSectorDiskMedia.ReadBytes(int byteOffset, int byteCount)
            => RequireSectorMedia().ReadBytes(byteOffset, byteCount);

        internal static int GetTrackIndex(int cylinder, int head)
        {
            if (cylinder < 0 || cylinder >= CylinderCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cylinder));
            }

            if (head < 0 || head >= HeadCount)
            {
                throw new ArgumentOutOfRangeException(nameof(head));
            }

            return (cylinder * HeadCount) + head;
        }

        internal static int GetLogicalSector(int cylinder, int head, int sector)
        {
            _ = GetTrackIndex(cylinder, head);
            if (sector < 0 || sector >= SectorsPerTrack)
            {
                throw new ArgumentOutOfRangeException(nameof(sector));
            }

            return ((cylinder * HeadCount) + head) * SectorsPerTrack + sector;
        }

        private IAmigaSectorDiskMedia RequireSectorMedia()
        {
            return _sectorMedia ?? throw new AmigaEmulationException($"Disk image '{Name}' does not expose standard AmigaDOS sectors.");
        }

        private static CoreEncodedTrack ToCoreTrack(IAmigaTrack track)
        {
            return new CoreEncodedTrack(
                track.EncodedData,
                track.BitLength,
                track.StartBit,
                ToCoreFeatures(track.Features));
        }

        private static CopperDiskEncodedTrack ToCopperDiskTrack(CoreEncodedTrack track)
        {
            return new CopperDiskEncodedTrack(
                track.EncodedData,
                track.BitLength,
                track.StartBit,
                ToCopperDiskFeatures(track.Features));
        }

        private static CoreTrackFeatures ToCoreFeatures(CopperDiskTrackFeatures features)
        {
            var converted = CoreTrackFeatures.None;
            if ((features & CopperDiskTrackFeatures.PreservedTrackData) != 0)
            {
                converted |= CoreTrackFeatures.PreservedTrackData;
            }

            if ((features & CopperDiskTrackFeatures.WeakData) != 0)
            {
                converted |= CoreTrackFeatures.WeakData;
            }

            if ((features & CopperDiskTrackFeatures.ApproximateWeakData) != 0)
            {
                converted |= CoreTrackFeatures.ApproximateWeakData;
            }

            return converted;
        }

        private static CopperDiskTrackFeatures ToCopperDiskFeatures(CoreTrackFeatures features)
        {
            var converted = CopperDiskTrackFeatures.None;
            if ((features & CoreTrackFeatures.PreservedTrackData) != 0)
            {
                converted |= CopperDiskTrackFeatures.PreservedTrackData;
            }

            if ((features & CoreTrackFeatures.WeakData) != 0)
            {
                converted |= CopperDiskTrackFeatures.WeakData;
            }

            if ((features & CoreTrackFeatures.ApproximateWeakData) != 0)
            {
                converted |= CopperDiskTrackFeatures.ApproximateWeakData;
            }

            return converted;
        }

        private static bool HasPreservedExtension(string displayName)
            => displayName.EndsWith(".ipf", StringComparison.OrdinalIgnoreCase);

        private static bool GetDefaultWriteProtected(string path, string displayName)
        {
            var pathExtension = Path.GetExtension(path);
            if (pathExtension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return pathExtension.Equals(".adf", StringComparison.OrdinalIgnoreCase) ||
                pathExtension.Equals(".adz", StringComparison.OrdinalIgnoreCase) ||
                pathExtension.Equals(".dms", StringComparison.OrdinalIgnoreCase) ||
                pathExtension.Equals(".ipf", StringComparison.OrdinalIgnoreCase) ||
                HasPreservedExtension(displayName);
        }

        private static AmigaDiskImage WrapDiskException(Func<AmigaDiskImage> action)
        {
            try
            {
                return action();
            }
            catch (AmigaDiskException ex)
            {
                throw new AmigaEmulationException(ex.Message);
            }
        }
    }
}
