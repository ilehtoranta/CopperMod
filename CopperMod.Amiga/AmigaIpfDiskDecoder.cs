using System;
using CopperMod.Ipf;

namespace CopperMod.Amiga
{
    internal static class AmigaIpfDiskDecoder
    {
        public static AmigaDiskImage DecodeBytes(byte[] data, string name)
        {
            ArgumentNullException.ThrowIfNull(data);
            try
            {
                var ipf = IpfDecoder.Decode(data);
                var tracks = new AmigaEncodedTrack[AmigaDiskImage.CylinderCount * AmigaDiskImage.HeadCount];
                foreach (var track in ipf.Tracks)
                {
                    if ((uint)track.Cylinder >= AmigaDiskImage.CylinderCount ||
                        (uint)track.Head >= AmigaDiskImage.HeadCount)
                    {
                        continue;
                    }

                    tracks[(track.Cylinder * AmigaDiskImage.HeadCount) + track.Head] = new AmigaEncodedTrack(track.Data, track.BitLength);
                }

                for (var index = 0; index < tracks.Length; index++)
                {
                    if (tracks[index].BitLength == 0)
                    {
                        tracks[index] = AmigaEncodedTrack.FromBytes(AmigaDosTrackEncoder.CreateUnformattedTrack());
                    }
                }

                return AmigaDiskImage.FromEncodedTracks(tracks, name);
            }
            catch (IpfDecodeException ex)
            {
                throw new AmigaEmulationException($"Unable to decode IPF disk image '{name}': {ex.Message}");
            }
        }
    }
}
