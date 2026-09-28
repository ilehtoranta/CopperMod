using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Guest-memory layouts used by the first graphics.library goals.
/// Values are byte offsets from the corresponding Amiga structure base.
/// </summary>
internal static class GraphicsLayouts
{
    // graphics/copper.h guest layouts.  CopIns is six bytes because the
    // pseudo-opcode is followed by the two-word move/wait union.  CopList
    // includes the V1_3 short-frame pointers used by Kickstart 3.1.
    internal const int CopInsSize = 0x06;
    internal const int CopInsOpCode = 0x00;
    internal const int CopInsArg0 = 0x02;
    internal const int CopInsArg1 = 0x04;
    // Canonical end marker for an OCS/ECS raw copper stream. The native
    // preallocated MakeVPort unit emits this pair without synthesizing any
    // display-register sequence.
    internal const ushort CopperEndWait = 0xFFFF;
    internal const ushort CopperEndMask = 0xFFFE;

    internal const int CopListSize = 0x36;
    internal const int CopListNext = 0x00;
    internal const int CopListSystem = 0x04;
    internal const int CopListViewPort = 0x08;
    internal const int CopListCopIns = 0x0C;
    internal const int CopListCopPtr = 0x10;
    internal const int CopListCopLStart = 0x14;
    internal const int CopListCopSStart = 0x18;
    internal const int CopListCount = 0x1C;
    internal const int CopListMaxCount = 0x1E;
    internal const int CopListDyOffset = 0x20;
    internal const int CopListCop2Start = 0x22;
    internal const int CopListCop3Start = 0x26;
    internal const int CopListCop4Start = 0x2A;
    internal const int CopListCop5Start = 0x2E;
    internal const int CopListSlRepeat = 0x32;
    internal const int CopListFlags = 0x34;

    internal const int UCopListSize = 0x0C;
    internal const int UCopListNext = 0x00;
    internal const int UCopListFirstCopList = 0x04;
    internal const int UCopListCopList = 0x08;

    // graphics/copper.h struct cprlist.
    internal const int CprListSize = 0x0C;
    internal const int CprListNext = 0x00;
    internal const int CprListStart = 0x04;
    internal const int CprListMaxCount = 0x08;

    internal const int ViewSize = 0x12;
    internal const int ViewViewPort = 0x00;
    internal const int ViewLoFCprList = 0x04;
    internal const int ViewShFCprList = 0x08;
    internal const int ViewDyOffset = 0x0C;
    internal const int ViewDxOffset = 0x0E;
    internal const int ViewModes = 0x10;
    // The standard OCS/ECS display origin encoded by the default DIWSTRT
    // (V=0x2C, H=0x81). InitView publishes these signed offsets after
    // clearing the View envelope; future profile-specific/native paths may
    // replace the values only through an explicit display backend.
    internal const short ViewDefaultDyOffset = 0x002C;
    internal const short ViewDefaultDxOffset = 0x0081;

    internal const int ViewPortSize = 0x28;
    internal const int ViewPortNext = 0x00;
    internal const int ViewPortColorMap = 0x04;
    internal const int ViewPortDspIns = 0x08;
    internal const int ViewPortSprIns = 0x0C;
    internal const int ViewPortClrIns = 0x10;
    internal const int ViewPortUCopIns = 0x14;
    internal const int ViewPortDWidth = 0x18;
    internal const int ViewPortDHeight = 0x1A;
    internal const int ViewPortDxOffset = 0x1C;
    internal const int ViewPortDyOffset = 0x1E;
    internal const int ViewPortModes = 0x20;
    internal const int ViewPortSpritePriorities = 0x22;
    // Kickstart's InitVPort publishes the default sprite-priority byte as
    // $24. Keep the value beside the resident field offset so the portable,
    // synthetic-screen, and future CopperSharp68k paths cannot drift apart.
    internal const byte ViewPortDefaultSpritePriorities = 0x24;
    internal const int ViewPortExtendedModes = 0x23;
    internal const int ViewPortRasInfo = 0x24;

    // intuition/screens.h and intuition/newscreen.h guest layouts. Keep the
    // public Screen prefix in the same shared contract as View/ViewPort so a
    // future CopperSharp68k-native image cannot drift onto host-local title or
    // font offsets. The nested ViewPort/RastPort/BitMap offsets are part of
    // this envelope and are intentionally asserted by the host lifecycle
    // tests as well.
    internal const int ScreenSize = 0x15A;
    internal const int ScreenFirstWindow = 0x04;
    internal const int ScreenLeftEdge = 0x08;
    internal const int ScreenTopEdge = 0x0A;
    internal const int ScreenWidth = 0x0C;
    internal const int ScreenHeight = 0x0E;
    internal const int ScreenFlags = 0x14;
    internal const int ScreenTitle = 0x16;
    internal const int ScreenDefaultTitle = 0x1A;
    internal const int ScreenFont = 0x28;
    internal const int ScreenViewPort = 0x2C;
    internal const int ScreenRastPort = 0x54;
    internal const int ScreenBitMap = 0xB8;
    internal const int ScreenDetailPen = 0x14A;
    internal const int ScreenBlockPen = 0x14B;
    internal const int NewScreenFont = 0x10;
    internal const int NewScreenDefaultTitle = 0x14;

    // Private native result record for Screen-prefix depths five through
    // eight. The caller supplies an even, writable guest envelope at A0;
    // the native allocator publishes the owned Screen/View/RasInfo/table and
    // the depth-sized plane list only after the complete transaction commits.
    internal const int ScreenPrefixExecResultScreen = 0x00;
    internal const int ScreenPrefixExecResultView = 0x04;
    internal const int ScreenPrefixExecResultRasInfo = 0x08;
    internal const int ScreenPrefixExecResultPlaneTable = 0x0C;
    internal const int ScreenPrefixExecResultPlane0 = 0x10;
    internal const int ScreenPrefixExecResultPlane1 = 0x14;
    internal const int ScreenPrefixExecResultPlane2 = 0x18;
    internal const int ScreenPrefixExecResultPlane3 = 0x1C;
    internal const int ScreenPrefixExecResultPlane4 = 0x20;
    internal const int ScreenPrefixExecResultSize = 0x24;
    // Extended result envelope used by the depth-six-through-eight owners.
    // The v1 five-plane record remains source-compatible; callers of the
    // extended ABI provide the larger envelope and receive plane five through
    // seven at the following longword offsets.
    internal const int ScreenPrefixExecResultPlane5 = 0x24;
    internal const int ScreenPrefixExecResultPlane6 = 0x28;
    internal const int ScreenPrefixExecResultPlane7 = 0x2C;
    internal const int ScreenPrefixExecResultExtendedSize = 0x30;

    // intuition/window.h and intuition/intuition.h guest prefixes.  These
    // fields are consumed by the synthetic screen/window bridge today and
    // remain in the shared contract so a CopperSharp68k-native implementation
    // cannot drift onto host-local Window offsets.
    internal const int NewWindowLeftEdge = 0x00;
    internal const int NewWindowTopEdge = 0x02;
    internal const int NewWindowWidth = 0x04;
    internal const int NewWindowHeight = 0x06;
    internal const int NewWindowIdcmpFlags = 0x0A;
    internal const int NewWindowFirstGadget = 0x12;
    // NewWindow keeps the caller-selected custom Screen after the title and
    // check-mark pointers.  The synthetic Intuition bridge accepts a null
    // target as its compatibility/default-screen form, but a non-null target
    // must be the screen owned by that bridge before OpenWindow can claim it.
    internal const int NewWindowScreen = 0x1E;
    internal const int NewWindowType = 0x2E;
    internal const int WindowSize = 0x58;
    internal const int WindowNext = 0x00;
    internal const int WindowWScreen = 0x2E;
    internal const int WindowRPort = 0x32;
    internal const int WindowFirstGadget = 0x3E;
    internal const int WindowIdcmpFlags = 0x52;
    internal const int WindowUserPort = 0x56;

    internal const int RasInfoSize = 0x0C;
    internal const int RasInfoNext = 0x00;
    internal const int RasInfoBitMap = 0x04;
    internal const int RasInfoRxOffset = 0x08;
    internal const int RasInfoRyOffset = 0x0A;

    internal const int DBufInfoSize = 0x54;
    internal const int DBufInfoLink1 = 0x00;
    internal const int DBufInfoCount1 = 0x04;
    internal const int DBufInfoSafeMessage = 0x08;
    internal const int DBufInfoSafeUserData = 0x1C;
    internal const int DBufInfoLink2 = 0x20;
    internal const int DBufInfoCount2 = 0x24;
    internal const int DBufInfoDispMessage = 0x28;
    internal const int DBufInfoDispUserData = 0x3C;
    internal const int DBufInfoMatchLong = 0x40;
    internal const int DBufInfoCopPtr1 = 0x44;
    internal const int DBufInfoCopPtr2 = 0x48;
    internal const int DBufInfoCopPtr3 = 0x4C;
    internal const int DBufInfoBeamPos1 = 0x50;
    internal const int DBufInfoBeamPos2 = 0x52;
    internal const int TmpRasRasPtr = 0x00;
    internal const int TmpRasByteCount = 0x04;
    internal const int TmpRasSize = 0x08;
    internal const int ExecMessageSize = 0x14;
    internal const int ExecMessageLength = 0x12;

    internal const int RectangleSize = 0x08;
    internal const int RectangleMinX = 0x00;
    internal const int RectangleMinY = 0x02;
    internal const int RectangleMaxX = 0x04;
    internal const int RectangleMaxY = 0x06;

    internal const int RegionSize = 0x0C;
    internal const int RegionBounds = 0x00;
    internal const int RegionRectangle = 0x08;
    internal const int RegionRectangleSize = 0x10;
    // RegionRectangle is a MinNode-compatible doubly linked guest list. The
    // predecessor of its first node is Region.RegionRectangle itself.
    internal const int RegionRectangleNext = 0x00;
    internal const int RegionRectanglePrevious = 0x04;
    internal const int RegionRectangleBounds = 0x08;
    // Native NewRegion keeps the public Region envelope unchanged while
    // reserving one longword immediately before it for the paired native
    // DisposeRegion ownership check.  The prefix is private to the native
    // CopperSharp68k-compatible path and is never exposed through the ABI.
    internal const int NativeRegionPrivatePrefixSize = 0x04;
    internal const int NativeRegionAllocationSize =
        RegionSize + NativeRegionPrivatePrefixSize;
    internal const ushort NativeRegionMarker = 0x5247;

    // Native ColorMap ownership envelope.  The public ColorMap follows a
    // private 12-byte prefix; one contiguous Exec allocation then contains
    // the ColorMap plus its high- and low-color tables.  The public pointers
    // retain the classic ABI while the prefix lets FreeColorMap prove that a
    // map belongs to this native allocator without a host-side registry.
    internal const int NativeColorMapPrivatePrefixSize = 0x0C;
    internal const int NativeColorMapBaseSize =
        NativeColorMapPrivatePrefixSize + ColorMapSize;
    internal const ushort NativeColorMapMarker = 0x434D;

    internal const int AreaInfoSize = 0x18;
    internal const int AreaInfoVectorTable = 0x00;
    internal const int AreaInfoVectorPointer = 0x04;
    internal const int AreaInfoFlagTable = 0x08;
    internal const int AreaInfoFlagPointer = 0x0C;
    internal const int AreaInfoCount = 0x10;
    internal const int AreaInfoMaxCount = 0x12;
    internal const int AreaInfoFirstX = 0x14;
    internal const int AreaInfoFirstY = 0x16;

    internal const int BitMapSize = 0x28;
    internal const int BitMapBytesPerRow = 0x00;
    internal const int BitMapRows = 0x02;
    internal const int BitMapFlags = 0x04;
    internal const int BitMapDepth = 0x05;
    internal const int BitMapPlanes = 0x08;

    // Classic packed RastPort, including its public reserved tail. Screen's
    // embedded BitMap starts immediately after these 100 bytes.
    internal const int RastPortSize = 0x64;
    // Transitional conservative admission extent for drawing/font/GELS
    // paths not yet migrated. This is not the public structure's size;
    // initializers must use RastPortSize and never clear this larger span.
    internal const int RastPortMinimumSize = 0xB4;
    internal const int RastPortLayer = 0x00;
    internal const int RastPortBitMap = 0x04;
    internal const int RastPortAreaPtrn = 0x08;
    internal const int RastPortTmpRas = 0x0C;
    internal const int RastPortAreaInfo = 0x10;
    internal const int RastPortGelsInfo = 0x14;
    internal const int RastPortMask = 0x18;
    internal const int RastPortFgPen = 0x19;
    internal const int RastPortBgPen = 0x1A;
    internal const int RastPortOutlinePen = 0x1B;
    internal const int RastPortDrawMode = 0x1C;
    internal const int RastPortAreaPtSz = 0x1D;
    internal const int RastPortLinePatternCount = 0x1E;
    internal const int RastPortFlags = 0x20;
    internal const int RastPortFirstDot = 0x0001;
    internal const int RastPortOneDot = 0x0002;
    internal const int RastPortAreaOutline = 0x0008;
    // Public graphics.library area-filler flag.  The portable AreaEnd path
    // already builds and applies one temporary mask per recorded shape, so
    // its shape transaction is the no-crossover form this bit requests.
    internal const int RastPortNoCrossFill = 0x0020;
    // Private graphics.library state used by SetAPen/SetBPen/SetDrMd and
    // SetABPenDrMd.  Kickstart keeps this bit in the public RastPort Flags
    // word even though it is not exposed by the public rastport.h header.
    internal const int RastPortNoPens = 0x4000;
    internal const int RastPortLinePattern = 0x22;
    internal const int RastPortCurrentX = 0x24;
    internal const int RastPortCurrentY = 0x26;
    internal const int RastPortMinterms = 0x28;
    internal const int RastPortPenWidth = 0x30;
    internal const int RastPortPenHeight = 0x32;
    internal const int RastPortFont = 0x34;
    internal const int RastPortAlgoStyle = 0x38;
    internal const int RastPortTextFlags = 0x39;
    internal const int RastPortTextHeight = 0x3A;
    internal const int RastPortTextWidth = 0x3C;
    internal const int RastPortTextBaseline = 0x3E;
    internal const int RastPortTextSpacing = 0x40;

    internal const int TextFontMinimumSize = 0x34;
    internal const int TextAttrSize = 0x08;
    internal const int TextAttrName = 0x00;
    internal const int TextAttrYSize = 0x04;
    internal const int TextAttrStyle = 0x06;
    internal const int TextAttrFlags = 0x07;
    internal const int TextFontNodeType = 0x08;
    internal const byte TextFontNodeTypeFont = 0x0C;
    internal const int TextFontName = 0x0A;
    internal const int TextFontExtension = 0x0E;
    internal const int TextFontYSize = 0x14;
    internal const int TextFontStyle = 0x16;
    internal const int TextFontFlags = 0x17;
    internal const int TextFontXSize = 0x18;
    internal const int TextFontBaseline = 0x1A;
    internal const int TextFontBoldSmear = 0x1C;
    internal const int TextFontAccessors = 0x1E;
    internal const int TextFontLoChar = 0x20;
    internal const int TextFontHiChar = 0x21;
    internal const int TextFontCharData = 0x22;
    internal const int TextFontModulo = 0x26;
    internal const int TextFontCharLoc = 0x28;
    internal const int TextFontCharSpace = 0x2C;
    internal const int TextFontCharKern = 0x30;
    internal const int TextFontExtensionSize = 0x18;
    internal const int TextFontExtensionMatchWord = 0x00;
    internal const int TextFontExtensionFlags0 = 0x02;
    internal const int TextFontExtensionFlags1 = 0x03;
    internal const int TextFontExtensionBackPtr = 0x04;
    internal const int TextFontExtensionOrigReplyPort = 0x08;
    internal const int TextFontExtensionTags = 0x0C;

    // ColorTextFont extends the public TextFont prefix at byte 0x34.  Keep
    // these offsets in the shared guest-layout contract so a CopperSharp68k
    // body and the portable memory decoder agree on the multi-plane strike
    // envelope.
    internal const int ColorTextFontFlags = 0x34;
    internal const int ColorTextFontDepth = 0x36;
    internal const int ColorTextFontForegroundColor = 0x37;
    internal const int ColorTextFontLowColor = 0x38;
    internal const int ColorTextFontHighColor = 0x39;
    internal const int ColorTextFontPlanePick = 0x3A;
    internal const int ColorTextFontPlaneOnOff = 0x3B;
    internal const int ColorTextFontColors = 0x3C;
    internal const int ColorTextFontCharacterData = 0x40;
    internal const int ColorTextFontPlanePointerCount = 8;
    internal const int ColorTextFontSize = 0x60;

    // The public prefix of graphics/gfxbase.h.  Library is 0x22 bytes on
    // 68k; three Interrupt nodes follow the four bltnode pointers, placing
    // the TextFonts List at 0x8C and DefaultFont at 0x9A.
    internal const int GfxBaseLibrarySize = 0x22;
    internal const int GfxBaseTextFonts = GraphicsLibraryImageLayout.GfxBaseTextFonts;
    internal const int GfxBaseTextFontsHead = GraphicsLibraryImageLayout.GfxBaseTextFontsHead;
    internal const int GfxBaseTextFontsTail = GraphicsLibraryImageLayout.GfxBaseTextFontsTail;
    internal const int GfxBaseTextFontsTailPred = GraphicsLibraryImageLayout.GfxBaseTextFontsTailPred;
    internal const int GfxBaseTextFontsType = GraphicsLibraryImageLayout.GfxBaseTextFontsType;
    internal const int GfxBaseTextFontsPad = GraphicsLibraryImageLayout.GfxBaseTextFontsPad;
    internal const int GfxBaseDefaultFont = GraphicsLibraryImageLayout.GfxBaseDefaultFont;
    internal const int GfxBasePublicPrefixSize = 0x9E;
    // Public/native fields used by LoadView and SetChipRev. Keep these in
    // the shared guest-layout contract so host and future native 68k paths
    // cannot drift onto host-only offsets.
    internal const int GfxBaseActiView = GraphicsLibraryImageLayout.GfxBaseActiView;
    // V39 capability byte; the host shim extends its envelope through this
    // field without claiming the private fields between the public prefix and
    // the full native GfxBase layout.
    internal const int GfxBaseChipRevBits0 = GraphicsLibraryImageLayout.GfxBaseChipRevBits0;
    // Native graphics/gfxbase.h monitor publication fields.  The optional
    // host/native bridge uses these only when a mapped full GfxBase envelope
    // is supplied; the compact compatibility image intentionally remains
    // untouched when it ends before the private tail of GfxBase.
    internal const int GfxBaseCurrentMonitor = GraphicsLibraryImageLayout.GfxBaseCurrentMonitor;
    internal const int GfxBaseMonitorList = GraphicsLibraryImageLayout.GfxBaseMonitorList;
    internal const int GfxBaseMonitorListHead = GraphicsLibraryImageLayout.GfxBaseMonitorListHead;
    internal const int GfxBaseMonitorListTail = GraphicsLibraryImageLayout.GfxBaseMonitorListTail;
    internal const int GfxBaseMonitorListTailPred = GraphicsLibraryImageLayout.GfxBaseMonitorListTailPred;
    internal const int GfxBaseMonitorListType = GraphicsLibraryImageLayout.GfxBaseMonitorListType;
    internal const int GfxBaseMonitorListPad = GraphicsLibraryImageLayout.GfxBaseMonitorListPad;
    internal const int GfxBaseDefaultMonitor = GraphicsLibraryImageLayout.GfxBaseDefaultMonitor;
    internal const int GfxBaseMonitorListSemaphore = GraphicsLibraryImageLayout.GfxBaseMonitorListSemaphore;
    // gfxbase.h declares DisplayInfoDataBase as an APTR to the native
    // display-database object, not an embedded Exec List.  Keep the pointer
    // offset explicit so a future native/provider implementation can publish
    // its own object without treating the following GfxBase fields as list
    // sentinels.
    internal const int GfxBaseDisplayInfoDataBase = GraphicsLibraryImageLayout.GfxBaseDisplayInfoDataBase;
    internal const int GfxBaseTopLine = GraphicsLibraryImageLayout.GfxBaseTopLine;
    internal const int GfxBaseActiViewCprSemaphore = GraphicsLibraryImageLayout.GfxBaseActiViewCprSemaphore;
    internal const int GfxBaseNativeSize = GraphicsLibraryImageLayout.GfxBaseNativeSize;

    internal const int TextExtentSize = 0x0C;
    internal const int TextExtentWidth = 0x00;
    internal const int TextExtentHeight = 0x02;
    internal const int TextExtentMinX = 0x04;
    internal const int TextExtentMinY = 0x06;
    internal const int TextExtentMaxX = 0x08;
    internal const int TextExtentMaxY = 0x0A;

    internal const int ColorMapSize = 0x34;
    internal const int ColorMapFlags = 0x00;
    internal const int ColorMapType = 0x01;
    internal const int ColorMapCount = 0x02;
    internal const int ColorMapColorTable = 0x04;
    internal const int ColorMapViewPortExtra = 0x08;
    internal const int ColorMapLowColorBits = 0x0C;
    internal const int ColorMapTransparencyPlane = 0x10;
    internal const int ColorMapSpriteResolution = 0x11;
    internal const int ColorMapSpriteResDefault = 0x12;
    internal const int ColorMapAuxFlags = 0x13;
    internal const int ColorMapViewPort = 0x14;
    internal const int ColorMapNormalDisplayInfo = 0x18;
    internal const int ColorMapCoerceDisplayInfo = 0x1C;
    internal const int ColorMapBatchItems = 0x20;
    internal const int ColorMapModeId = 0x24;
    internal const int ColorMapPaletteExtra = 0x28;
    internal const int ColorMapSpriteBaseEven = 0x2C;
    internal const int ColorMapSpriteBaseOdd = 0x2E;
    internal const int ColorMapBitPlane0Base = 0x30;
    internal const int ColorMapBitPlane1Base = 0x32;

    // hardware/blit.h struct bltnode.  The classic 68k headers keep the
    // UBYTE/short sequence packed, so the public prefix is 17 bytes.
    internal const int BltNodeSize = 0x11;
    internal const int BltNodeNext = 0x00;
    internal const int BltNodeFunction = 0x04;
    internal const int BltNodeStatus = 0x08;
    internal const int BltNodeBlitSize = 0x09;
    internal const int BltNodeBeamSync = 0x0B;
    internal const int BltNodeCleanup = 0x0D;

    // graphics/view.h PaletteExtra. The public 68k SignalSemaphore prefix is
    // 0x2E bytes; these fields follow it without host pointer widening.
    internal const int PaletteExtraSemaphoreNodeType = 0x08;
    internal const int PaletteExtraSemaphoreNestCount = 0x0E;
    internal const int PaletteExtraSemaphoreWaitQueue = 0x10;
    internal const int PaletteExtraSemaphoreOwner = 0x28;
    internal const int PaletteExtraSemaphoreQueueCount = 0x2C;
    internal const int PaletteExtraSemaphoreSize = 0x2E;
    internal const int PaletteExtraFirstFree = 0x2E;
    internal const int PaletteExtraNFree = 0x30;
    internal const int PaletteExtraFirstShared = 0x32;
    internal const int PaletteExtraNShared = 0x34;
    internal const int PaletteExtraRefCount = 0x36;
    internal const int PaletteExtraAllocList = 0x3A;
    internal const int PaletteExtraViewPort = 0x3E;
    // Kickstart stores the highest sharable pen index here (0..N inclusive),
    // despite the field's descriptive "number of sharable colors" name.
    internal const int PaletteExtraSharableColors = 0x42;
    internal const int PaletteExtraSize = 0x44;

    internal const int SimpleSpriteSize = 0x0C;
    internal const int SimpleSpritePosCtlData = 0x00;
    internal const int SimpleSpriteHeight = 0x04;
    internal const int SimpleSpriteX = 0x06;
    internal const int SimpleSpriteY = 0x08;
    internal const int SimpleSpriteNum = 0x0A;

    // graphics/sprite.h ExtSprite and sprite-image envelope.  The V39
    // public ExtSprite is the SimpleSprite prefix followed by two words.
    internal const int ExtSpriteSize = 0x10;
    internal const int ExtSpriteWordWidth = 0x0C;
    internal const int ExtSpriteFlags = 0x0E;
    internal const int SpriteImagePosCtlSize = 0x04;
    internal const int SpriteImageReservedSize = 0x04;

    // graphics/sprite.h allocation/query tags.
    internal const uint SpriteAWidth = 0x8100_0000;
    internal const uint SpriteAXReplication = 0x8100_0002;
    internal const uint SpriteAYReplication = 0x8100_0004;
    internal const uint SpriteAOutputHeight = 0x8100_0006;
    internal const uint SpriteAAttached = 0x8100_0008;
    internal const uint SpriteAOldDataFormat = 0x8100_000A;
    internal const uint GsTagSpriteNum = 0x8200_0020;
    internal const uint GsTagAttached = 0x8200_0022;
    internal const uint GsTagSoftSprite = 0x8200_0024;
    internal const uint GsTagScanDoubled = 0x8300_0000;
    internal const ushort SpriteAttachedFlag = 0x0080;

    // graphics/gels.h standard VSprite and graphics/rastport.h GelsInfo
    // layouts.  VUserStuff is the default WORD, so the public VSprite ends at
    // 0x3C; callers that compile a larger private extension remain outside
    // this portable standard envelope.
    internal const int VSpriteSize = 0x3C;
    internal const int VSpriteNext = 0x00;
    internal const int VSpritePrev = 0x04;
    internal const int VSpriteDrawPath = 0x08;
    internal const int VSpriteClearPath = 0x0C;
    internal const int VSpriteOldY = 0x10;
    internal const int VSpriteOldX = 0x12;
    internal const int VSpriteFlags = 0x14;
    internal const int VSpriteY = 0x16;
    internal const int VSpriteX = 0x18;
    internal const int VSpriteHeight = 0x1A;
    internal const int VSpriteWidth = 0x1C;
    internal const int VSpriteDepth = 0x1E;
    internal const int VSpriteMeMask = 0x20;
    internal const int VSpriteHitMask = 0x22;
    internal const int VSpriteImageData = 0x24;
    internal const int VSpriteBorderLine = 0x28;
    internal const int VSpriteCollMask = 0x2C;
    internal const int VSpriteSprColors = 0x30;
    internal const int VSpriteVSBob = 0x34;
    internal const int VSpritePlanePick = 0x38;
    internal const int VSpritePlaneOnOff = 0x39;
    internal const int VSpriteUserExt = 0x3A;
    internal const ushort VSpriteFlag = 0x0001;
    // graphics/gels.h divides VSprite.Flags into caller-owned low bits and
    // system-maintained status bits.  AddVSprite starts a fresh system pass,
    // so the status portion must not leak from an earlier list membership.
    internal const ushort VSpriteUserFlags = 0x00FF;
    internal const ushort VSpriteSystemFlags = 0x0F00;

    internal const int GelsInfoSize = 0x26;
    internal const int GelsInfoSpriteReserved = 0x00;
    internal const int GelsInfoFlags = 0x01;
    internal const int GelsInfoHead = 0x02;
    internal const int GelsInfoTail = 0x06;
    internal const int GelsInfoNextLine = 0x0A;
    internal const int GelsInfoLastColor = 0x0E;
    internal const int GelsInfoCollisionHandler = 0x12;
    internal const int GelsInfoLeftmost = 0x16;
    internal const int GelsInfoRightmost = 0x18;
    internal const int GelsInfoTopmost = 0x1A;
    internal const int GelsInfoBottommost = 0x1C;
    internal const int GelsInfoFirstBlissObj = 0x1E;
    internal const int GelsInfoLastBlissObj = 0x22;
    internal const int CollisionTableEntries = 16;
    internal const int CollisionTableSize = CollisionTableEntries * 4;

    // graphics/gels.h animation envelopes.  These are the classic 68k
    // layouts (pointer fields remain four bytes); the optional user-extension
    // words are included so native CopperSharp68k callers can use the same
    // addresses without a host-side wrapper structure.
    internal const int BobSize = 0x20;
    internal const int BobFlags = 0x00;
    internal const int BobSaveBuffer = 0x04;
    internal const int BobImageShadow = 0x08;
    internal const int BobBefore = 0x0C;
    internal const int BobAfter = 0x10;
    internal const int BobVSprite = 0x14;
    internal const int BobComp = 0x18;
    internal const int BobDBuffer = 0x1C;
    internal const ushort BobFlagSaveBob = 0x0001;
    internal const ushort BobFlagIsComp = 0x0002;
    internal const ushort BobFlagWaiting = 0x0100;
    internal const ushort BobFlagDrawn = 0x0200;
    internal const ushort BobFlagAway = 0x0400;
    internal const ushort BobFlagNix = 0x0800;
    internal const ushort BobFlagSavePreserve = 0x1000;
    internal const ushort BobFlagOutStep = 0x2000;

    internal const ushort VSpriteSaveBack = 0x0002;
    internal const ushort VSpriteOverlay = 0x0004;
    internal const ushort VSpriteMustDraw = 0x0008;
    internal const ushort VSpriteBackSaved = 0x0100;
    internal const ushort VSpriteUpdate = 0x0200;
    internal const ushort VSpriteGelGone = 0x0400;
    internal const ushort VSpriteOverflow = 0x0800;

    internal const int AnimCompSize = 0x26;
    internal const int AnimCompFlags = 0x00;
    internal const int AnimCompTimer = 0x02;
    internal const int AnimCompTimeSet = 0x04;
    internal const int AnimCompNextComp = 0x06;
    internal const int AnimCompPrevComp = 0x0A;
    internal const int AnimCompNextSeq = 0x0E;
    internal const int AnimCompPrevSeq = 0x12;
    internal const int AnimCompRoutine = 0x16;
    internal const int AnimCompYTrans = 0x1A;
    internal const int AnimCompXTrans = 0x1C;
    internal const int AnimCompHeadOb = 0x1E;
    internal const int AnimCompAnimBob = 0x22;
    internal const ushort AnimCompFlagBackward = 0x0001;
    internal const ushort AnimCompFlagReversible = 0x0002;
    internal const ushort AnimCompFlagNoBob = 0x0004;

    internal const int AnimObSize = 0x2A;
    internal const int AnimObNextOb = 0x00;
    internal const int AnimObPrevOb = 0x04;
    internal const int AnimObClock = 0x08;
    internal const int AnimObOldY = 0x0C;
    internal const int AnimObOldX = 0x0E;
    internal const int AnimObY = 0x10;
    internal const int AnimObX = 0x12;
    internal const int AnimObYVel = 0x14;
    internal const int AnimObXVel = 0x16;
    internal const int AnimObXAccel = 0x18;
    internal const int AnimObYAccel = 0x1A;
    internal const int AnimObRingYTrans = 0x1C;
    internal const int AnimObRingXTrans = 0x1E;
    internal const int AnimObRoutine = 0x20;
    internal const int AnimObHeadComp = 0x24;

    internal const int DBufPacketSize = 0x10;
    internal const int DBufPacketY = 0x00;
    internal const int DBufPacketX = 0x02;
    internal const int DBufPacketPath = 0x04;
    internal const int DBufPacketBuffer = 0x08;
    internal const int DBufPacketPlanes = 0x0C;

    // graphics/monitor.h MonitorSpec (68k guest layout).  The monitor node
    // contains private driver fields after this public prefix; native display
    // code owns those fields, while portable OpenMonitor only initializes the
    // deterministic PAL/NTSC state needed by ViewExtra and display queries.
    internal const int MonitorSpecSize = 0xA0;
    internal const int MonitorSpecNodeType = 0x08;
    internal const int MonitorSpecNodePriority = 0x09;
    internal const int MonitorSpecNodeName = 0x0A;
    internal const int MonitorSpecNodeSubsystem = 0x0E;
    internal const int MonitorSpecNodeSubtype = 0x0F;
    internal const int MonitorSpecFlags = 0x18;
    internal const int MonitorSpecRatioH = 0x1A;
    internal const int MonitorSpecRatioV = 0x1E;
    internal const int MonitorSpecTotalRows = 0x22;
    internal const int MonitorSpecTotalColorClocks = 0x24;
    internal const int MonitorSpecDeniseMaxDisplayColumn = 0x26;
    internal const int MonitorSpecBeamCon0 = 0x28;
    internal const int MonitorSpecMinRow = 0x2A;
    internal const int MonitorSpecSpecial = 0x2C;
    internal const int MonitorSpecOpenCount = 0x30;
    internal const int MonitorSpecTransform = 0x32;
    internal const int MonitorSpecTranslate = 0x36;
    internal const int MonitorSpecScale = 0x3A;
    internal const int MonitorSpecXOffset = 0x3E;
    internal const int MonitorSpecYOffset = 0x40;
    internal const int MonitorSpecLegalView = 0x42;
    internal const int MonitorSpecMaxOScan = 0x4A;
    internal const int MonitorSpecVideoScan = 0x4E;
    internal const int MonitorSpecDeniseMinDisplayColumn = 0x52;
    internal const int MonitorSpecDisplayCompatible = 0x54;
    internal const int MonitorSpecDisplayInfoDataBase = 0x58;
    // Embedded Exec List sentinels in MonitorSpec.DisplayInfoDataBase.
    internal const int MonitorSpecDisplayInfoDataBaseHead = 0x58;
    internal const int MonitorSpecDisplayInfoDataBaseTail = 0x5C;
    internal const int MonitorSpecDisplayInfoDataBaseTailPred = 0x60;
    internal const int MonitorSpecDisplayInfoDataBaseType = 0x64;
    internal const int MonitorSpecDisplayInfoDataBasePad = 0x65;
    internal const int MonitorSpecDisplayInfoSemaphore = 0x66;
    // SignalSemaphore prefix embedded after MonitorSpec.DisplayInfoDataBase.
    // Keep the offsets explicit for the native 68k guest layout; the prefix
    // ends at 0x94 immediately before the monitor callback fields.
    internal const int MonitorSpecDisplayInfoSemaphoreNodeType =
        MonitorSpecDisplayInfoSemaphore + PaletteExtraSemaphoreNodeType;
    internal const int MonitorSpecDisplayInfoSemaphoreNestCount =
        MonitorSpecDisplayInfoSemaphore + PaletteExtraSemaphoreNestCount;
    internal const int MonitorSpecDisplayInfoSemaphoreWaitQueue =
        MonitorSpecDisplayInfoSemaphore + PaletteExtraSemaphoreWaitQueue;
    internal const int MonitorSpecDisplayInfoSemaphoreOwner =
        MonitorSpecDisplayInfoSemaphore + PaletteExtraSemaphoreOwner;
    internal const int MonitorSpecDisplayInfoSemaphoreQueueCount =
        MonitorSpecDisplayInfoSemaphore + PaletteExtraSemaphoreQueueCount;
    internal const int MonitorSpecDisplayInfoSemaphoreSize =
        PaletteExtraSemaphoreSize;
    internal const int MonitorSpecMergeCopper = 0x94;
    internal const int MonitorSpecLoadView = 0x98;
    internal const int MonitorSpecKillView = 0x9C;

    // graphics/view.h extended nodes returned by GfxNew.  The final sizes are
    // longword-aligned C guest structures (the public fields end on a word
    // boundary, but the allocator contract remains longword-safe).
    internal const int ExtendedNodeSize = 0x18;
    internal const int ExtendedNodeType = 0x08;
    internal const int ExtendedNodeName = 0x0A;
    internal const int ExtendedNodeSubsystem = 0x0E;
    internal const int ExtendedNodeSubtype = 0x0F;
    internal const int ExtendedNodeLibrary = 0x10;
    internal const int ExtendedNodeInit = 0x14;
    internal const int ViewExtraSize = 0x24;
    internal const int ViewExtraView = 0x18;
    internal const int ViewExtraMonitor = 0x1C;
    internal const int ViewExtraTopLine = 0x20;
    internal const int ViewPortExtraSize = 0x44;
    internal const int ViewPortExtraViewPort = 0x18;
    internal const int ViewPortExtraDisplayClip = 0x1C;
    internal const int ViewPortExtraVecTable = 0x24;
    internal const int ViewPortExtraDriverData0 = 0x28;
    internal const int ViewPortExtraDriverData1 = 0x2C;
    internal const int ViewPortExtraFlags = 0x30;
    internal const int ViewPortExtraOrigin0 = 0x32;
    internal const int ViewPortExtraOrigin1 = 0x36;
    internal const int ViewPortExtraCop1Ptr = 0x3A;
    internal const int ViewPortExtraCop2Ptr = 0x3E;
    // graphics/monitor.h SpecialMonitor.  The public structure ends after
    // four AnalogSignalInterval records; driver callback execution remains a
    // native/provider responsibility.
    internal const int SpecialMonitorSize = 0x3A;

    // graphics/scale.h BitScaleArgs (68k, word-aligned guest layout).
    internal const int BitScaleArgsSize = 0x30;
    internal const int BitScaleArgsSrcX = 0x00;
    internal const int BitScaleArgsSrcY = 0x02;
    internal const int BitScaleArgsSrcWidth = 0x04;
    internal const int BitScaleArgsSrcHeight = 0x06;
    internal const int BitScaleArgsXSrcFactor = 0x08;
    internal const int BitScaleArgsYSrcFactor = 0x0A;
    internal const int BitScaleArgsDestX = 0x0C;
    internal const int BitScaleArgsDestY = 0x0E;
    internal const int BitScaleArgsDestWidth = 0x10;
    internal const int BitScaleArgsDestHeight = 0x12;
    internal const int BitScaleArgsXDestFactor = 0x14;
    internal const int BitScaleArgsYDestFactor = 0x16;
    internal const int BitScaleArgsSrcBitMap = 0x18;
    internal const int BitScaleArgsDestBitMap = 0x1C;
    internal const int BitScaleArgsFlags = 0x20;
    internal const int BitScaleArgsXDDA = 0x24;
    internal const int BitScaleArgsYDDA = 0x26;
    internal const int BitScaleArgsReserved1 = 0x28;
    internal const int BitScaleArgsReserved2 = 0x2C;
}
