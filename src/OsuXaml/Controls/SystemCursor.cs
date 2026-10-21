using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using osu.Framework.Extensions.EnumExtensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using SDL;
using static SDL.SDL3;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Puts a <see cref="Cursor"/> on the operating system's pointer. osu!framework only shows, hides and confines the pointer,
    /// so the shape goes straight to SDL3, on the window's thread (the host's input thread) as SDL requires.
    /// Each popup layer that tracks the pointer asks for the shape over its area; the last one to ask holds the pointer
    /// until it lets go, which puts the arrow back.
    /// </summary>
    internal static unsafe class SystemCursor
    {
        private sealed class State
        {
            public object Owner;
            public CursorType Shown = CursorType.Arrow;
            public bool HidPointer;

            // SDL cursors are made once per shape and kept, as SDL_SetCursor doesn't take ownership. Window thread only.
            public readonly Dictionary<SDL_SystemCursor, nint> Created = new Dictionary<SDL_SystemCursor, nint>();
        }

        private static readonly ConditionalWeakTable<GameHost, State> states = new ConditionalWeakTable<GameHost, State>();

        private static bool unavailable;

        /// <summary>Shows <paramref name="cursor"/> on behalf of <paramref name="owner"/>. Call from the update thread.</summary>
        public static void Request(GameHost host, object owner, Cursor cursor)
        {
            var state = states.GetValue(host, _ => new State());
            state.Owner = owner;
            show(host, state, cursor.CursorType);
        }

        /// <summary>Puts the arrow back if <paramref name="owner"/> was the last to set the pointer. Call from the update thread.</summary>
        public static void Release(GameHost host, object owner)
        {
            if (!states.TryGetValue(host, out var state) || state.Owner != owner)
                return;

            state.Owner = null;
            show(host, state, CursorType.Arrow);
        }

        private static void show(GameHost host, State state, CursorType type)
        {
            if (state.Shown == type || host.Window is not IWindow window)
                return;

            state.Shown = type;

            // None hides the pointer through osu!framework, so a pointer the game hid itself stays hidden afterwards.
            if (type == CursorType.None)
            {
                if (!window.CursorState.HasFlagFast(CursorState.Hidden))
                {
                    window.CursorState |= CursorState.Hidden;
                    state.HidPointer = true;
                }

                return;
            }

            if (state.HidPointer)
            {
                window.CursorState &= ~CursorState.Hidden;
                state.HidPointer = false;
            }

            var shape = sdlCursor(type);
            host.InputThread.Scheduler.Add(() => apply(state, shape));
        }

        private static void apply(State state, SDL_SystemCursor? shape)
        {
            if (unavailable)
                return;

            try
            {
                // Without an SDL3 window (the SDL2 backend) there is nothing to set.
                if ((SDL_WasInit(SDL_InitFlags.SDL_INIT_VIDEO) & SDL_InitFlags.SDL_INIT_VIDEO) == 0)
                    return;

                SDL_Cursor* cursor = null;

                if (shape is SDL_SystemCursor id)
                {
                    if (!state.Created.TryGetValue(id, out nint created))
                        state.Created[id] = created = (nint)SDL_CreateSystemCursor(id);

                    cursor = (SDL_Cursor*)created;
                }

                // A shape this platform lacks falls back to the arrow.
                SDL_SetCursor(cursor != null ? cursor : SDL_GetDefaultCursor());
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                unavailable = true;
                Logger.Log($"Mouse cursor shapes are unavailable: {e.Message}");
            }
        }

        private static SDL_SystemCursor? sdlCursor(CursorType type)
        {
            switch (type)
            {
                case CursorType.No: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_NOT_ALLOWED;
                case CursorType.AppStarting: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_PROGRESS;
                case CursorType.Cross: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_CROSSHAIR;
                case CursorType.Help: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_HELP;
                case CursorType.IBeam: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_TEXT;
                case CursorType.SizeAll: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_MOVE;
                case CursorType.SizeNESW: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_NESW_RESIZE;
                case CursorType.SizeNS: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_NS_RESIZE;
                case CursorType.SizeNWSE: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_NWSE_RESIZE;
                case CursorType.SizeWE: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_EW_RESIZE;
                case CursorType.Wait: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_WAIT;
                case CursorType.Hand: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_POINTER;
                case CursorType.ScrollAll: return SDL_SystemCursor.SDL_SYSTEM_CURSOR_ALL_SCROLL;
                default: return null;
            }
        }
    }
}
