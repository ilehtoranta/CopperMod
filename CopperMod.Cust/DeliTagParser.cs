using System;
using System.Collections.Generic;
using System.Text;

namespace CopperMod.Cust
{
    internal sealed class DeliTagTable
    {
        public DeliTagTable(int segmentIndex, int offset, IReadOnlyDictionary<uint, uint> values)
        {
            SegmentIndex = segmentIndex;
            Offset = offset;
            Values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public int SegmentIndex { get; }

        public int Offset { get; }

        public IReadOnlyDictionary<uint, uint> Values { get; }

        public uint this[uint tag] => Values.TryGetValue(tag, out var value) ? value : 0;
    }

    internal static class DeliTagParser
    {
        private static readonly uint[] RequiredTags =
        {
            CustConstants.DtpPlayerVersion,
            CustConstants.DtpInitPlayer,
            CustConstants.DtpInitSound
        };

        private static readonly uint[] SupportedIdentityTags =
        {
            CustConstants.DtpCheck,
            CustConstants.DtpInterrupt,
            CustConstants.DtpCheck2,
            CustConstants.DtpSubSongRange,
            CustConstants.DtpEndPlayer,
            CustConstants.DtpEndSound,
            CustConstants.DtpVolume,
            CustConstants.DtpBalance,
            CustConstants.DtpVoices,
            CustConstants.DtpModuleInfo,
            CustConstants.DtpSampleInfo,
            CustConstants.DtpFlags
        };

        public static bool TryFindTags(HunkFile hunk, out DeliTagTable tags)
        {
            foreach (var segment in hunk.Segments)
            {
                if (segment.Kind != HunkSegmentKind.Code && segment.Kind != HunkSegmentKind.Data)
                {
                    continue;
                }

                var data = segment.Data;
                for (var offset = 0; offset <= data.Length - 16; offset += 2)
                {
                    if (BigEndian.ReadUInt32(data, offset, "tag") != CustConstants.DtpPlayerVersion)
                    {
                        continue;
                    }

                    if (TryReadTagTable(data, segment.Index, offset, out tags) && HasRequiredTags(tags))
                    {
                        return true;
                    }
                }
            }

            tags = new DeliTagTable(0, 0, new Dictionary<uint, uint>());
            return false;
        }

        public static string? ExtractVersionTitle(HunkFile hunk)
        {
            foreach (var segment in hunk.Segments)
            {
                var text = Encoding.Latin1.GetString(segment.Data);
                var marker = text.IndexOf("$VER:", StringComparison.OrdinalIgnoreCase);
                if (marker < 0)
                {
                    continue;
                }

                var end = text.IndexOf('\0', marker);
                if (end < 0)
                {
                    end = Math.Min(text.Length, marker + 96);
                }

                var value = text.Substring(marker + 5, end - marker - 5).Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }

        private static bool TryReadTagTable(byte[] data, int segmentIndex, int offset, out DeliTagTable tags)
        {
            var values = new Dictionary<uint, uint>();
            var cursor = offset;
            while (cursor + 8 <= data.Length && values.Count < 64)
            {
                var tag = BigEndian.ReadUInt32(data, cursor, "DeliTracker tag");
                cursor += 4;
                if (tag == CustConstants.TagDone)
                {
                    tags = new DeliTagTable(segmentIndex, offset, values);
                    return values.Count > 0;
                }

                var value = BigEndian.ReadUInt32(data, cursor, "DeliTracker tag value");
                cursor += 4;
                if ((tag & 0xFFFF_0000) != 0x8000_0000)
                {
                    break;
                }

                values[tag] = value;
            }

            tags = new DeliTagTable(segmentIndex, offset, values);
            return false;
        }

        private static bool HasRequiredTags(DeliTagTable tags)
        {
            foreach (var required in RequiredTags)
            {
                if (!tags.Values.ContainsKey(required))
                {
                    return false;
                }
            }

            foreach (var supported in SupportedIdentityTags)
            {
                if (tags.Values.ContainsKey(supported))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
