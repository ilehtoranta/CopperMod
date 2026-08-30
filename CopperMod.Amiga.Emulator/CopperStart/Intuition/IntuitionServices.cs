using Amiga;
using Copper68k;
using PortableIntuition = CopperStart.Intuition;

namespace CopperMod.Amiga.CopperStart.Intuition;

/// <summary>Register adapter for the portable Intuition router and host graphics/UI capability seam.</summary>
internal sealed class IntuitionServices
{
	private readonly CopperStartIntuitionContext _context;
	public IntuitionServices(CopperStartIntuitionContext context) => _context = context;
	public void Invoke(M68kCpuState state, int lvo)
	{
		_context.LogCall(lvo); var p = new IntuitionHostPlatform(_context, state);
		state.D[0] = PortableIntuition.IntuitionVectorCore.Invoke(ref p, (short)lvo, state.D[0], state.D[1], state.D[2], state.A[0], state.A[1], state.A[2], state.A[3], state.Cycles);
	}
}

internal readonly struct IntuitionHostPlatform : PortableIntuition.IIntuitionPlatform
{
	private readonly CopperStartIntuitionContext _context; private readonly M68kCpuState _state;
	public IntuitionHostPlatform(CopperStartIntuitionContext context, M68kCpuState state) { _context = context; _state = state; }
	public APTR NextObject(APTR objectPointerAddress) =>
		_context.NextObject is null
			? APTR.Null
			: APTR.FromPointer(_context.NextObject(objectPointerAddress.Raw));
	public uint InvokeCapability(short lvo, uint d0, uint d1, uint d2, uint a0, uint a1, uint a2, uint a3, long cycles)
	{
		switch (lvo)
		{
			case IntuitionLvo.OpenScreen:
				// NewScreen contains WORD/LONG fields.  A non-null odd base is
				// therefore a 68000 address-error envelope, not a host-compatible
				// request; leave it to native/provider ownership before any screen
				// configuration or display callback can observe it.
				if (a0 != 0 && (a0 & 1u) != 0)
					return 0;
				if (a0 != 0 &&
					_context.ValidateNewScreenAddress is not null &&
					!_context.ValidateNewScreenAddress(a0))
					return 0;

				_context.ConfigureScreen(a0);
				var screen = _context.EnsureScreen();
				if (!_context.CompleteScreenOpen(screen))
					return 0;

				// OpenScreen only commits a real guest Screen.  Keep a permissive
				// host completion callback from turning a NULL handoff into a
				// successful finalization; the concrete synthetic owner normally
				// rejects this in CompleteScreenOpen, but the boundary must remain
				// safe for CopperSharp68k and provider adapters as well.
				if (screen == 0)
					return 0;

				if (!_context.TryPrepareScreenResources(
						screen,
						out var graphicsResourcesPrepared))
				{
						var rollback = new M68kCpuState { A = { [0] = screen } };
						_context.CloseScreen(rollback);
						_context.FinalizeScreenOpen?.Invoke(false);
						return 0;
				}

				if (_context.RethinkDisplay(cycles) != 0)
				{
					// OpenScreen is an atomic public lifecycle boundary: the
					// caller receives a Screen only after its viewport has been
					// reconstructed and linked into the active View.  If the
					// display owner rejects that reconstruction, retire the
					// just-created Screen before returning NULL instead of
					// leaking a half-open guest session into a later retry.
					_context.FinalizePreparedScreenResources(screen, false);
					var rollback = new M68kCpuState { A = { [0] = screen } };
					_context.CloseScreen(rollback);
					// Finalization is request-scoped rather than conditional on the
					// teardown result.  A successful CloseScreen rollback still has
					// to release staged palette/error state; an unclosable foreign
					// object remains available to native teardown while the failed
					// request is nevertheless retired here.
					_context.FinalizeScreenOpen?.Invoke(false);
					return 0;
				}

				// The display owner must still expose the exact Screen that was
				// staged before reconstruction.  A successful rethink may have
				// retired or replaced that handoff; do not finalize or return the
				// stale Screen, and release its claim through the same rollback
				// boundary used for an explicit rethink failure.
				if (_context.EnsureScreen() != screen)
				{
					_context.FinalizePreparedScreenResources(screen, false);
					var rollback = new M68kCpuState { A = { [0] = screen } };
					_context.CloseScreen(rollback);
					_context.FinalizeScreenOpen?.Invoke(false);
					return 0;
				}

				if (graphicsResourcesPrepared)
					_context.FinalizePreparedScreenResources(screen, true);
				_context.FinalizeScreenOpen?.Invoke(true);
				return screen;
			case IntuitionLvo.CloseScreen:
				// Screen contains WORD/LONG fields.  Preserve the live frame for a
				// native/provider address-error envelope instead of letting a mapped
				// odd pointer reach synthetic teardown.
				if (_state.A[0] != 0 && (_state.A[0] & 1u) != 0)
					return _state.D[0];
				if (_state.A[0] != 0 &&
					_context.ValidateScreenAddress is not null &&
					!_context.ValidateScreenAddress(_state.A[0]))
					return _state.D[0];
				var closingScreen = _state.A[0];
				_context.CloseScreen(_state);
				// Resource retirement is subordinate to the actual Screen teardown.
				// A declined CloseScreen (for example, because a Window or a foreign
				// ViewPort link is still attached) leaves the committed graphics chain
				// owned by that Screen for a later retry; only a successful close may
				// release it. Failed OpenScreen paths retain their explicit rollback
				// finalization above, before they attempt to close the staged Screen.
				if (_state.D[0] != 0)
					_context.FinalizePreparedScreenResources(closingScreen, false);
				return _state.D[0];
			case IntuitionLvo.OpenScreenTagList:
				// Both the optional legacy NewScreen and the TagItem chain are
				// word-addressed guest structures.  Do not let byte-addressable
				// host memory turn either odd pointer into a claimed open.
				if ((a0 != 0 && (a0 & 1u) != 0) ||
					(a1 != 0 && (a1 & 1u) != 0))
					return 0;
				if (a0 != 0 &&
					_context.ValidateNewScreenAddress is not null &&
					!_context.ValidateNewScreenAddress(a0))
					return 0;

				if (!_context.ConfigureScreenTags(a0, a1))
					return 0;

				var tagged = _context.EnsureScreen();
				if (!_context.CompleteScreenOpen(tagged))
					return 0;

				// Match OpenScreen's admission rule for the tag-list form: a
				// permissive host completion callback cannot publish success for a
				// NULL Screen handoff.
				if (tagged == 0)
					return 0;

				if (!_context.TryPrepareScreenResources(
						tagged,
						out var taggedGraphicsResourcesPrepared))
				{
						var rollback = new M68kCpuState { A = { [0] = tagged } };
						_context.CloseScreen(rollback);
						_context.FinalizeScreenOpen?.Invoke(false);
						return 0;
				}

				if (_context.RethinkDisplay(cycles) != 0)
				{
					// Keep the tag-list form identical to OpenScreen: a failed
					// RethinkDisplay must not publish a Screen pointer or leave
					// its allocations attached to the compatibility session.
					_context.FinalizePreparedScreenResources(tagged, false);
					var rollback = new M68kCpuState { A = { [0] = tagged } };
					_context.CloseScreen(rollback);
					_context.FinalizeScreenOpen?.Invoke(false);
					return 0;
				}

				// Keep the tag-list form on the same exact-identity admission
				// boundary as OpenScreen.  A replacement Screen cannot inherit a
				// successful finalization for the staged request.
				if (_context.EnsureScreen() != tagged)
				{
					_context.FinalizePreparedScreenResources(tagged, false);
					var rollback = new M68kCpuState { A = { [0] = tagged } };
					_context.CloseScreen(rollback);
					_context.FinalizeScreenOpen?.Invoke(false);
					return 0;
				}

				if (taggedGraphicsResourcesPrepared)
					_context.FinalizePreparedScreenResources(tagged, true);
				_context.FinalizeScreenOpen?.Invoke(true);
				return tagged;
			case IntuitionLvo.OpenWindow:
				// NewWindow contains WORD/LONG fields and must be word-aligned in
				// guest memory.  Keep the null compatibility form, but never let a
				// byte-addressable odd envelope reach host configuration callbacks.
				if (a0 != 0 && (a0 & 1u) != 0)
					return 0;
				if (a0 != 0 &&
					_context.ValidateNewWindowAddress is not null &&
					!_context.ValidateNewWindowAddress(a0))
					return 0;
				_context.ConfigureWindow(a0);
				var window = _context.OpenWindow();
				if (window == 0)
					return 0;

				// OpenWindow is successful only after Intuition has incorporated the
				// window's screen into the active View.  A missing Screen is itself a
				// failed handoff, even when a host's empty-view rethink reports no
				// error; otherwise a later CloseScreen remains blocked by a window
				// the caller never received.
				var windowScreen = _context.EnsureScreen();
				if (windowScreen == 0)
				{
					_context.CloseWindow(window);
					return 0;
				}

				if (_context.RethinkDisplay(cycles) != 0)
				{
					_context.CloseWindow(window);
					return 0;
				}

				// The display owner may retire or replace the active Screen while
				// reconstructing the View.  A successful rethink is not enough to
				// publish a Window if the handoff no longer has a real Screen; keep
				// the Window claim atomic so a later CloseScreen cannot be blocked by
				// an object the caller never received.
				if (_context.EnsureScreen() != windowScreen)
				{
					_context.CloseWindow(window);
					return 0;
				}

				return window;
			case IntuitionLvo.CloseWindow:
				// Window contains WORD/LONG fields.  Leave an odd non-null pointer
				// to native/provider address-error ownership instead of forwarding
				// byte-addressable storage into synthetic teardown.
				if (a0 != 0 && (a0 & 1u) != 0)
					return 0;
				if (a0 != 0 &&
					_context.ValidateWindowAddress is not null &&
					!_context.ValidateWindowAddress(a0))
					return 0;
				_context.CloseWindow(a0);
				return 0;
			case IntuitionLvo.ModifyIDCMP:
				// ModifyIDCMP consumes a Window * in A0.  A mapped odd envelope is
				// an address-error/provider request, not a synthetic input update.
				if (_state.A[0] != 0 && (_state.A[0] & 1u) != 0)
					return _state.D[0];
				if (_state.A[0] != 0 &&
					_context.ValidateWindowAddress is not null &&
					!_context.ValidateWindowAddress(_state.A[0]))
					return _state.D[0];
				if (_context.TryModifyIdcmp is not null)
					return _context.TryModifyIdcmp(_state) ? 1u : _state.D[0];
				_context.ModifyIdcmp(_state);
				return 1;
			case IntuitionLvo.ReportMouse: return 0;
			case IntuitionLvo.ScreenToFront:
				if (a0 != 0 && (a0 & 1u) == 0)
				{
					if (_context.ValidateScreenAddress is not null &&
						!_context.ValidateScreenAddress(a0))
						return 0;

					// A state-aware activation callback is an explicit lifecycle
					// owner.  A decline must remain a decline so a foreign or
					// provider-owned Screen cannot be claimed by the legacy
					// viewport selector after that owner has rejected it.
					if (_context.ActivateScreenState is not null)
					{
						_ = _context.ActivateScreenState(a0, _state);
						return 0;
					}

					if (_context.ActivateScreen(a0, _state.Cycles))
						return 0;

					// Screen.ViewPort is a guest LONG at offset 0x2C.  Do not
					// wrap that derived pointer into low memory when a mapped
					// high-address Screen is offered to the legacy selector; the
					// native/provider owner must retain the address-error envelope.
					if (a0 > uint.MaxValue - 0x2Cu)
						return 0;

					_context.SelectFrontViewPort(a0 + 0x2C);
				}
				// Screen contains WORD/LONG fields and is therefore a word-aligned
				// 68000 envelope.  Do not let byte-addressable host memory turn an
				// odd pointer into a legacy viewport-selection claim; native Intuition
				// or a provider must retain the address-error request.
				return 0;
			case IntuitionLvo.ShowTitle:
				// ShowTitle consumes a Screen * in A0; keep odd envelopes outside
				// the synthetic title-bar owner and available to native Intuition.
				if (_state.A[0] != 0 && (_state.A[0] & 1u) != 0)
					return 0;
				if (_state.A[0] != 0 &&
					_context.ValidateScreenAddress is not null &&
					!_context.ValidateScreenAddress(_state.A[0]))
					return 0;
				_context.ShowTitle(_state);
				return 0;
			case IntuitionLvo.SetWindowTitles:
				// SetWindowTitles consumes a Window * in A0.  The title strings in
				// A1/A2 are byte-addressed and are intentionally not aligned here.
				if (_state.A[0] != 0 && (_state.A[0] & 1u) != 0)
					return _state.D[0];
				if (_state.A[0] != 0 &&
					_context.ValidateWindowAddress is not null &&
					!_context.ValidateWindowAddress(_state.A[0]))
					return _state.D[0];
				_context.SetWindowTitles(_state);
				// The provider owns this void vector's D0 clobber. Forward its
				// live result, including an unchanged D0 when a synthetic request
				// is declined; neither force zero nor restore the input sentinel.
				return _state.D[0];
			case IntuitionLvo.GetScreenData:
				// A1 is the optional Screen *; A0 is a byte-addressed destination
				// buffer and may legally be odd.
				if (_state.A[1] != 0 && (_state.A[1] & 1u) != 0)
					return _state.D[0];
				if (_state.A[1] != 0 &&
					_context.ValidateScreenAddress is not null &&
					!_context.ValidateScreenAddress(_state.A[1]))
					return _state.D[0];
				_context.GetScreenData(_state);
				return _state.D[0];
			case IntuitionLvo.QueryOverscan:
				// Rectangle contains WORD fields and is therefore word-aligned; the
				// display ID in A0 and overscan selector in D0 are scalar values.
				if (_state.A[1] != 0 && (_state.A[1] & 1u) != 0)
					return _state.D[0];
				_context.QueryOverscan(_state);
				return _state.D[0];
			case IntuitionLvo.MakeScreen: case IntuitionLvo.RemakeDisplay: case IntuitionLvo.RethinkDisplay:
				return _context.RethinkDisplayWithState?.Invoke(_state) ?? _context.RethinkDisplay(cycles);
			case IntuitionLvo.AllocRemember: _context.AllocRemember(_state); return _state.D[0];
			case IntuitionLvo.FreeRemember: case IntuitionLvo.RefreshGList: return 0;
			case IntuitionLvo.ViewAddress: var view = _context.GetViewAddress(); return view != 0 ? view : _context.EnsureView();
			case IntuitionLvo.ViewPortAddress:
				// ViewPortAddress consumes a Window * in A0 and returns the viewport
				// of that window's WScreen. A null pointer keeps the compatibility
				// lookup; when a host supplies the native-shaped owner, a non-null
				// request must use that window-specific boundary rather than silently
				// returning the active screen's viewport for an unrelated Window.
				if (a0 != 0 && (a0 & 1u) != 0)
					return 0;
				if (a0 != 0 &&
					_context.ValidateWindowAddress is not null &&
					!_context.ValidateWindowAddress(a0))
					return 0;
				if (a0 != 0 && _context.GetWindowViewPortAddress is not null)
					return _context.GetWindowViewPortAddress(a0);
				return _context.GetViewPortAddress();
			case IntuitionLvo.AddGList:
				// AddGList carries Window *, Gadget *, and Requester * in A0-A2.
				// Keep all three structure envelopes on the same word-aligned
				// admission boundary before host list publication.  A Window
				// validator, when supplied by the compatibility owner, also
				// rejects a high public prefix whose fields would wrap into low
				// memory; native/provider owners remain available when it declines.
				if ((_state.A[0] != 0 && (_state.A[0] & 1u) != 0) ||
					(_state.A[1] != 0 && (_state.A[1] & 1u) != 0) ||
					(_state.A[2] != 0 && (_state.A[2] & 1u) != 0))
					return _state.D[0];
				if (_state.A[0] != 0 &&
					_context.ValidateWindowAddress is not null &&
					!_context.ValidateWindowAddress(_state.A[0]))
					return _state.D[0];
				_context.AddGList(_state);
				return _state.D[0];
			default: return _context.EnsureHostObject();
		}
	}
}
