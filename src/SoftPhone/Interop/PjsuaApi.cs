using System.Runtime.InteropServices;

namespace SoftPhone.Interop;

/// <summary>
/// P/Invoke declarations for pjsip_wrapper.dll native interop layer.
/// </summary>
internal static class PjsuaApi
{
    private const string DllName = "pjsip_wrapper.dll";

    /* ─── Callback delegates ─── */

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public delegate void OnRegStateCallback(int accId, int code,
        [MarshalAs(UnmanagedType.LPStr)] string reason);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public delegate void OnIncomingCallCallback(int accId, int callId,
        [MarshalAs(UnmanagedType.LPStr)] string fromUri,
        [MarshalAs(UnmanagedType.LPStr)] string toUri);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public delegate void OnCallStateCallback(int callId, int state,
        [MarshalAs(UnmanagedType.LPStr)] string stateText, int lastStatus);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void OnCallMediaStateCallback(int callId, int mediaStatus);

    /* ─── Core ─── */

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void softphone_set_callbacks(
        OnRegStateCallback onReg,
        OnIncomingCallCallback onIncoming,
        OnCallStateCallback onCall,
        OnCallMediaStateCallback onMedia);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_init();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void softphone_destroy();

    /* ─── Registration ─── */

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern int softphone_register(
        [MarshalAs(UnmanagedType.LPStr)] string server,
        [MarshalAs(UnmanagedType.LPStr)] string user,
        [MarshalAs(UnmanagedType.LPStr)] string password,
        int port);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void softphone_unregister();

    /* ─── Call control ─── */

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern int softphone_make_call(
        [MarshalAs(UnmanagedType.LPStr)] string destUri);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_answer_call(int callId, int code);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_hangup_call(int callId);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_hangup_all();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_set_hold(int callId, int hold);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_set_mute(int callId, int mute);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern int softphone_send_dtmf(int callId,
        [MarshalAs(UnmanagedType.LPStr)] string digits);

    /* ─── Audio ─── */

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_set_rx_level(int callId, float level);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_set_tx_level(int callId, float level);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_get_sound_device_count();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern int softphone_get_sound_device_name(int index,
        [MarshalAs(UnmanagedType.LPStr)] System.Text.StringBuilder nameBuf, int bufLen);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_set_sound_devices(int captureDev, int playbackDev);

    /* ─── Info ─── */

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_get_call_info(int callId,
        out int state, out int mediaStatus, out int durationSec);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int softphone_get_reg_status(out int isRegistered);

    /* ─── Transfer ─── */

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern int softphone_transfer_call(int callId,
        [MarshalAs(UnmanagedType.LPStr)] string destUri);

    /* ─── PJSIP call states (matches pjsip_inv_state) ─── */
    public const int PJSIP_INV_STATE_NULL       = 0;
    public const int PJSIP_INV_STATE_CALLING     = 1;
    public const int PJSIP_INV_STATE_INCOMING    = 2;
    public const int PJSIP_INV_STATE_EARLY       = 3;
    public const int PJSIP_INV_STATE_CONNECTING  = 4;
    public const int PJSIP_INV_STATE_CONFIRMED   = 5;
    public const int PJSIP_INV_STATE_DISCONNECTED = 6;

    /* ─── PJSUA media status ─── */
    public const int PJSUA_CALL_MEDIA_NONE    = 0;
    public const int PJSUA_CALL_MEDIA_ACTIVE  = 1;
    public const int PJSUA_CALL_MEDIA_LOCAL_HOLD = 2;
    public const int PJSUA_CALL_MEDIA_REMOTE_HOLD = 3;
}
