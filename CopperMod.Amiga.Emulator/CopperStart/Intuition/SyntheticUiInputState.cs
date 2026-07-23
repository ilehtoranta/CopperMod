/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Intuition;

/// <summary>Reset-scoped input and IDCMP queue state for the synthetic CopperStart session.</summary>
internal sealed class SyntheticUiInputState
{
    private readonly Queue<SyntheticIntuiMessage> _messages = new();

    public int MouseX { get; private set; }
    public int MouseY { get; private set; }
    public bool PrimaryMousePressed { get; private set; }
    public int MessageCount => _messages.Count;

    public void Reset(int mouseX, int mouseY)
    {
        _messages.Clear();
        MouseX = mouseX;
        MouseY = mouseY;
        PrimaryMousePressed = false;
    }

    public void SetMousePosition(int x, int y, int screenWidth, int screenHeight)
    {
        MouseX = Math.Clamp(x, 0, Math.Max(0, screenWidth - 1));
        MouseY = Math.Clamp(y, 0, Math.Max(0, screenHeight - 1));
    }

    public void SetPrimaryMousePressed(bool pressed) => PrimaryMousePressed = pressed;

    public void Enqueue(SyntheticIntuiMessage message) => _messages.Enqueue(message);

    public bool TryDequeue(out SyntheticIntuiMessage message) => _messages.TryDequeue(out message);

    public bool TryPeek(out SyntheticIntuiMessage message) => _messages.TryPeek(out message);
}

/// <summary>Guest-visible contents of a pending synthetic IDCMP message.</summary>
internal readonly struct SyntheticIntuiMessage
{
    public SyntheticIntuiMessage(uint messageClass, ushort code, ushort qualifier, uint iAddress, int mouseX, int mouseY, long cycles)
    {
        Class = messageClass;
        Code = code;
        Qualifier = qualifier;
        IAddress = iAddress;
        MouseX = mouseX;
        MouseY = mouseY;
        Cycles = cycles;
    }

    public uint Class { get; }
    public ushort Code { get; }
    public ushort Qualifier { get; }
    public uint IAddress { get; }
    public int MouseX { get; }
    public int MouseY { get; }
    public long Cycles { get; }
}
