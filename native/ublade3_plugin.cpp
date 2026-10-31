// UBlade3 native engine plugin: runs the exact HyperBlade 20XX engine inside the Unity player,
// with no external process. It renders the original 640x480 software frame (or the 640x480 menu
// canvas) into a BGRA buffer Unity uploads to a texture, and feeds Unity input back as SDL events.
#include "common/compat.h"
#include <SDL3/SDL.h>
#include <cstdlib>
#include <cstring>
#include <direct.h>
#include <vector>
#include "app/gameapp.h"
#include "app/window.h"
#include "globals.h"
#include "engine/gameengine.h"
#include "graphics/bitmapformat.h"
#include "graphics/graphicssettings.h"
#include "graphics/graphicsglobals.h"
#include "graphics/hudlayer.h"

namespace
{
std::vector<unsigned char> s_pixels;
int s_width = 640;
int s_height = 480;

bool PluginMode()
{
    return std::getenv("UBLADE3_PLUGIN") != nullptr;
}

void PushKey(int nScancode, bool bDown)
{
    SDL_Event event{};
    event.type = bDown ? SDL_EVENT_KEY_DOWN : SDL_EVENT_KEY_UP;
    event.key.scancode = static_cast<SDL_Scancode>(nScancode);
    event.key.key = SDL_GetKeyFromScancode(static_cast<SDL_Scancode>(nScancode), SDL_KMOD_NONE, true);
    event.key.down = bDown ? true : false;
    event.key.repeat = false;
    SDL_PushEvent(&event);
}

void CaptureMenu()
{
    const auto canvas = Window::MenuPixels();
    if (canvas.pBits == nullptr || canvas.nWidth <= 0 || canvas.nHeight <= 0)
    {
        return;
    }

    s_width = canvas.nWidth;
    s_height = canvas.nHeight;
    s_pixels.resize(static_cast<size_t>(s_width) * static_cast<size_t>(s_height) * 4);

    for (int y = 0; y < s_height; y++)
    {
        for (int x = 0; x < s_width; x++)
        {
            const auto n = canvas.pBits[static_cast<size_t>(y) * static_cast<size_t>(s_width) + x];
            const auto at = (static_cast<size_t>(y) * static_cast<size_t>(s_width) + x) * 4;
            s_pixels[at + 0] = static_cast<unsigned char>(n & 0xff);
            s_pixels[at + 1] = static_cast<unsigned char>((n >> 8) & 0xff);
            s_pixels[at + 2] = static_cast<unsigned char>((n >> 16) & 0xff);
            s_pixels[at + 3] = 255;
        }
    }
}

void CaptureMatch()
{
    if (g_pBackBufferBitsCopy == nullptr || g_nBackBufferWidth <= 0 || g_nBackBufferHeight <= 0)
    {
        return;
    }

    s_width = g_nBackBufferWidth;
    s_height = g_nBackBufferHeight;
    s_pixels.resize(static_cast<size_t>(s_width) * static_cast<size_t>(s_height) * 4);

    const auto* const palette = g_bmiSoftBackBuffer.bmiColors;
    const auto* const source = g_pBackBufferBitsCopy;
    const auto pitch = g_nSurfacePitchCopy > 0 ? g_nSurfacePitchCopy : g_nBackBufferWidth;

    for (int y = 0; y < s_height; y++)
    {
        for (int x = 0; x < s_width; x++)
        {
            const auto* const colour = palette + source[static_cast<size_t>(y) * static_cast<size_t>(pitch) + x];
            const auto at = (static_cast<size_t>(y) * static_cast<size_t>(s_width) + x) * 4;
            s_pixels[at + 0] = colour->rgbBlue;
            s_pixels[at + 1] = colour->rgbGreen;
            s_pixels[at + 2] = colour->rgbRed;
            s_pixels[at + 3] = 255;
        }
    }

    // The HUD is drawn into its own 8-bit layer; composite it as the presenter does.
    const auto& hud = HudLayer::Pixels();

    if (hud.pBits != nullptr)
    {
        for (int ly = 0; ly < hud.nHeight; ly++)
        {
            for (int lx = 0; lx < hud.nWidth; lx++)
            {
                const auto index = hud.pBits[static_cast<size_t>(ly) * static_cast<size_t>(hud.nPitch) + lx];

                if (index == HudLayer::kKeyIndex)
                {
                    continue;
                }

                const auto* const colour = palette + index;

                for (int sy = 0; sy < hud.nScale; sy++)
                {
                    const int y = hud.nOriginY + ly * hud.nScale + sy;

                    if (y < 0 || y >= s_height)
                    {
                        continue;
                    }

                    for (int sx = 0; sx < hud.nScale; sx++)
                    {
                        const int x = hud.nOriginX + lx * hud.nScale + sx;

                        if (x < 0 || x >= s_width)
                        {
                            continue;
                        }

                        const auto at = (static_cast<size_t>(y) * static_cast<size_t>(s_width) + x) * 4;
                        s_pixels[at + 0] = colour->rgbBlue;
                        s_pixels[at + 1] = colour->rgbGreen;
                        s_pixels[at + 2] = colour->rgbRed;
                        s_pixels[at + 3] = 255;
                    }
                }
            }
        }
    }
}
}

extern "C"
{
__declspec(dllexport) int UBlade3_Initialize(const char* pszGameRoot, const char* pszPakDir)
{
    _putenv_s("UBLADE3_PLUGIN", "1");
    _putenv_s("UBLADE3_GAME_ROOT", pszGameRoot == nullptr ? "" : pszGameRoot);
    _putenv_s("UBLADE3_PAK_DIR", pszPakDir == nullptr ? "" : pszPakDir);
    if (pszGameRoot != nullptr)
    {
        _chdir(pszGameRoot);
    }

    if (!GameApp::Initialize(""))
    {
        return 0;
    }

    SDL_HideWindow(Window::Native().As<SDL_Window>());
    s_pixels.assign(640 * 480 * 4, 0);
    s_width = 640;
    s_height = 480;
    CaptureMenu();
    return 1;
}

__declspec(dllexport) int UBlade3_Frame()
{
    if (!Window::Pump())
    {
        return 0;
    }

    // A hidden SDL window never gains focus; the plugin is the only host, so it is always active.
    g_gameApp.m_bIsActive = 1;

    GameApp::MainUpdate();

    if (g_gameApp.m_nEngineState == kEstateMatchLive)
    {
        CaptureMatch();
    }
    else
    {
        CaptureMenu();
    }

    return 1;
}

__declspec(dllexport) const unsigned char* UBlade3_Pixels()
{
    return s_pixels.empty() ? nullptr : s_pixels.data();
}

__declspec(dllexport) int UBlade3_RunState()
{
    return g_pGameEngine == nullptr ? -1 : static_cast<int>(g_pGameEngine->m_wRunState);
}

__declspec(dllexport) int UBlade3_State()
{
    return static_cast<int>(g_gameApp.m_nEngineState);
}

__declspec(dllexport) int UBlade3_NonBlack()
{
    int nCount = 0;

    for (size_t i = 0; i < s_pixels.size(); i += 4)
    {
        if (s_pixels[i] != 0 || s_pixels[i + 1] != 0 || s_pixels[i + 2] != 0)
        {
            nCount++;
        }
    }

    return nCount;
}

__declspec(dllexport) int UBlade3_Width()
{
    return s_width;
}

__declspec(dllexport) int UBlade3_Height()
{
    return s_height;
}

__declspec(dllexport) int UBlade3_Key(int nScancode, int bDown)
{
    PushKey(nScancode, bDown != 0);
    return 1;
}

__declspec(dllexport) int UBlade3_MouseMove(int nX, int nY)
{
    SDL_Event event{};
    event.type = SDL_EVENT_MOUSE_MOTION;
    event.motion.x = static_cast<float>(nX);
    event.motion.y = static_cast<float>(nY);
    event.motion.xrel = 0;
    event.motion.yrel = 0;
    event.motion.state = SDL_GetMouseState(nullptr, nullptr);
    SDL_PushEvent(&event);
    return 1;
}

__declspec(dllexport) int UBlade3_MouseButton(int nButton, int bDown, int nX, int nY)
{
    UBlade3_MouseMove(nX, nY);

    SDL_Event event{};
    event.type = bDown ? SDL_EVENT_MOUSE_BUTTON_DOWN : SDL_EVENT_MOUSE_BUTTON_UP;
    event.button.button = static_cast<Uint8>(nButton);
    event.button.down = bDown ? true : false;
    event.button.clicks = 1;
    event.button.x = static_cast<float>(nX);
    event.button.y = static_cast<float>(nY);
    SDL_PushEvent(&event);
    return 1;
}

__declspec(dllexport) int UBlade3_Shutdown()
{
    Window::Destroy();
    return 1;
}
}
