using System;

namespace AmigaTracker.ProTracker
{
    internal static class ProTrackerConstants
    {
        public const int ChannelCount = 4;
        public const int RowsPerPattern = 64;
        public const int PatternCellLength = 4;
        public const int PatternRowLength = ChannelCount * PatternCellLength;
        public const int PatternLength = RowsPerPattern * PatternRowLength;
        public const int ProTrackerHeaderLength = 1084;
        public const int LegacyHeaderLength = 600;
        public const int ProTrackerSampleCount = 31;
        public const int LegacySampleCount = 15;
        public const int SampleHeaderLength = 30;
        public const int DefaultSpeed = 6;
        public const int DefaultBpm = 125;
        public const int MinPeriod = 113;
        public const int MaxPeriod = 856;
        public const int PalColorClock = 4_433_618;
        public const double PalCpuClock = 7_093_789.2;
        public const double PalCiaClock = 709_378.92;
        public const double PalBpmTimerDiv = 1_773_447.0;
        public const double PalHorizontalHz = 15_625.0;
        public const double DmaWaitSeconds = 7.0 / PalHorizontalHz;

        public static readonly ushort[] NormalPeriods =
        {
            856, 808, 762, 720, 678, 640, 604, 570, 538, 508, 480, 453,
            428, 404, 381, 360, 339, 320, 302, 285, 269, 254, 240, 226,
            214, 202, 190, 180, 170, 160, 151, 143, 135, 127, 120, 113, 0
        };

        public static readonly ushort[][] FineTunePeriods =
        {
            new ushort[] { 856,808,762,720,678,640,604,570,538,508,480,453,428,404,381,360,339,320,302,285,269,254,240,226,214,202,190,180,170,160,151,143,135,127,120,113,0 },
            new ushort[] { 850,802,757,715,674,637,601,567,535,505,477,450,425,401,379,357,337,318,300,284,268,253,239,225,213,201,189,179,169,159,150,142,134,126,119,113,0 },
            new ushort[] { 844,796,752,709,670,632,597,563,532,502,474,447,422,398,376,355,335,316,298,282,266,251,237,224,211,199,188,177,167,158,149,141,133,125,118,112,0 },
            new ushort[] { 838,791,746,704,665,628,592,559,528,498,470,444,419,395,373,352,332,314,296,280,264,249,235,222,209,198,187,176,166,157,148,140,132,125,118,111,0 },
            new ushort[] { 832,785,741,699,660,623,588,555,524,495,467,441,416,392,370,350,330,312,294,278,262,247,233,220,208,196,185,175,165,156,147,139,131,124,117,110,0 },
            new ushort[] { 826,779,736,694,655,619,584,551,520,491,463,437,413,390,368,347,328,309,292,276,260,245,232,219,206,195,184,174,164,155,146,138,130,123,116,109,0 },
            new ushort[] { 820,774,730,689,651,614,580,547,516,487,460,434,410,387,365,345,325,307,290,274,258,244,230,217,205,193,183,172,163,154,145,137,129,122,115,109,0 },
            new ushort[] { 814,768,725,684,646,610,575,543,513,484,457,431,407,384,363,342,323,305,288,272,256,242,228,216,204,192,181,171,161,152,144,136,128,121,114,108,0 },
            new ushort[] { 907,856,808,762,720,678,640,604,570,538,508,480,453,428,404,381,360,339,320,302,285,269,254,240,226,214,202,190,180,170,160,151,143,135,127,120,0 },
            new ushort[] { 900,850,802,757,715,675,636,601,567,535,505,477,450,425,401,379,357,337,318,300,284,268,253,238,225,212,200,189,179,169,159,150,142,134,126,119,0 },
            new ushort[] { 894,844,796,752,709,670,632,597,563,532,502,474,447,422,398,376,355,335,316,298,282,266,251,237,223,211,199,188,177,167,158,149,141,133,125,118,0 },
            new ushort[] { 887,838,791,746,704,665,628,592,559,528,498,470,444,419,395,373,352,332,314,296,280,264,249,235,222,209,198,187,176,166,157,148,140,132,125,118,0 },
            new ushort[] { 881,832,785,741,699,660,623,588,555,524,494,467,441,416,392,370,350,330,312,294,278,262,247,233,220,208,196,185,175,165,156,147,139,131,123,117,0 },
            new ushort[] { 875,826,779,736,694,655,619,584,551,520,491,463,437,413,390,368,347,328,309,292,276,260,245,232,219,206,195,184,174,164,155,146,138,130,123,116,0 },
            new ushort[] { 868,820,774,730,689,651,614,580,547,516,487,460,434,410,387,365,345,325,307,290,274,258,244,230,217,205,193,183,172,163,154,145,137,129,122,115,0 },
            new ushort[] { 862,814,768,725,684,646,610,575,543,513,484,457,431,407,384,363,342,323,305,288,272,256,242,228,216,203,192,181,171,161,152,144,136,128,121,114,0 }
        };

        public static readonly byte[] ArpeggioTickTable =
        {
            0,1,2,0,1,2,0,1,
            2,0,1,2,0,1,2,0,
            1,2,0,1,2,0,1,2,
            0,1,2,0,1,2,0,1
        };

        public static readonly byte[] VibratoTable =
        {
            0, 24, 49, 74, 97, 120, 141, 161,
            180, 197, 212, 224, 235, 244, 250, 253,
            255, 253, 250, 244, 235, 224, 212, 197,
            180, 161, 141, 120, 97, 74, 49, 24
        };

        public static readonly byte[] FunkTable =
        {
            0, 5, 6, 7, 8, 10, 11, 13, 16, 19, 22, 26, 32, 43, 64, 128
        };

        public static bool IsProTrackerSignature(ReadOnlySpan<byte> data, int offset)
        {
            if (!ModEndian.HasRange(data, offset, 4))
            {
                return false;
            }

            return Matches(data, offset, "M.K.")
                || Matches(data, offset, "M!K!")
                || Matches(data, offset, "4CHN");
        }

        public static bool Matches(ReadOnlySpan<byte> data, int offset, string value)
        {
            if (!ModEndian.HasRange(data, offset, value.Length))
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (data[offset + i] != (byte)value[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
