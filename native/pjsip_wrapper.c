/*
 * pjsip_wrapper.c - C wrapper around PJSUA for C# P/Invoke interop
 * Exposes a simplified API for the WPF SoftPhone application.
 */

#include <pjsua-lib/pjsua.h>
#include <string.h>
#include <stdio.h>

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

/* ─── Callback function pointer types ─── */
typedef void(__stdcall *on_reg_state_callback)(int acc_id, int code, const char *reason);
typedef void(__stdcall *on_incoming_call_callback)(int acc_id, int call_id, const char *from_uri, const char *to_uri);
typedef void(__stdcall *on_call_state_callback)(int call_id, int state, const char *state_text, int last_status);
typedef void(__stdcall *on_call_media_state_callback)(int call_id, int media_status);

/* ─── Global state ─── */
static on_reg_state_callback       g_on_reg_state       = NULL;
static on_incoming_call_callback   g_on_incoming_call   = NULL;
static on_call_state_callback      g_on_call_state      = NULL;
static on_call_media_state_callback g_on_call_media     = NULL;

static pj_bool_t g_initialized = PJ_FALSE;
static int g_acc_id = PJSUA_INVALID_ID;
static int g_transport_id = -1;

/* ─── Stored server info for building call URIs ─── */
static char g_server[256] = {0};
static int  g_port = 5060;

/* ─── Helper: create pj_str_t from C string ─── */
static pj_str_t make_pj_str(const char *str) {
    pj_str_t s;
    s.ptr = (char *)str;
    s.slen = str ? (pj_ssize_t)strlen(str) : 0;
    return s;
}

/* ─── PJSUA Callbacks ─── */

static void cb_on_reg_state2(pjsua_acc_id acc_id, pjsua_reg_info *info) {
    if (g_on_reg_state) {
        int code = info->cbparam->code;
        char reason_buf[256] = {0};
        if (info->cbparam->reason.slen > 0) {
            int len = info->cbparam->reason.slen < 255 ? (int)info->cbparam->reason.slen : 255;
            memcpy(reason_buf, info->cbparam->reason.ptr, len);
        }
        g_on_reg_state(acc_id, code, reason_buf);
    }
}

static void cb_on_incoming_call(pjsua_acc_id acc_id, pjsua_call_id call_id,
                                 pjsip_rx_data *rdata) {
    pjsua_call_info ci;
    pjsua_call_get_info(call_id, &ci);

    /* Auto-answer with 180 Ringing */
    pjsua_call_answer(call_id, 180, NULL, NULL);

    if (g_on_incoming_call) {
        char from_buf[512] = {0};
        char to_buf[512] = {0};
        int from_len = ci.remote_info.slen < 511 ? (int)ci.remote_info.slen : 511;
        int to_len = ci.local_info.slen < 511 ? (int)ci.local_info.slen : 511;
        memcpy(from_buf, ci.remote_info.ptr, from_len);
        memcpy(to_buf, ci.local_info.ptr, to_len);
        g_on_incoming_call(acc_id, call_id, from_buf, to_buf);
    }
}

static void cb_on_call_state(pjsua_call_id call_id, pjsip_event *e) {
    pjsua_call_info ci;
    pjsua_call_get_info(call_id, &ci);

    if (g_on_call_state) {
        char state_text[128] = {0};
        int len = ci.state_text.slen < 127 ? (int)ci.state_text.slen : 127;
        memcpy(state_text, ci.state_text.ptr, len);
        g_on_call_state(call_id, (int)ci.state, state_text, (int)ci.last_status);
    }
}

static void cb_on_call_media_state(pjsua_call_id call_id) {
    pjsua_call_info ci;
    pjsua_call_get_info(call_id, &ci);

    /* Connect audio when media is active */
    if (ci.media_status == PJSUA_CALL_MEDIA_ACTIVE) {
        pjsua_conf_connect(ci.conf_slot, 0);
        pjsua_conf_connect(0, ci.conf_slot);
    }

    if (g_on_call_media) {
        g_on_call_media(call_id, (int)ci.media_status);
    }
}

/* ─── Exported API ─── */

EXPORT void __stdcall softphone_set_callbacks(
    on_reg_state_callback       on_reg,
    on_incoming_call_callback   on_incoming,
    on_call_state_callback      on_call,
    on_call_media_state_callback on_media)
{
    g_on_reg_state     = on_reg;
    g_on_incoming_call = on_incoming;
    g_on_call_state    = on_call;
    g_on_call_media    = on_media;
}

EXPORT int __stdcall softphone_init(void) {
    pj_status_t status;

    if (g_initialized) return 0;

    status = pjsua_create();
    if (status != PJ_SUCCESS) return (int)status;

    /* Configure pjsua */
    {
        pjsua_config cfg;
        pjsua_logging_config log_cfg;
        pjsua_media_config media_cfg;

        pjsua_config_default(&cfg);
        cfg.cb.on_reg_state2 = &cb_on_reg_state2;
        cfg.cb.on_incoming_call = &cb_on_incoming_call;
        cfg.cb.on_call_state = &cb_on_call_state;
        cfg.cb.on_call_media_state = &cb_on_call_media_state;
        cfg.max_calls = 4;

        pjsua_logging_config_default(&log_cfg);
        log_cfg.console_level = 4;
        log_cfg.level = 5;

        pjsua_media_config_default(&media_cfg);
        media_cfg.clock_rate = 8000;       /* Standard SIP/PCMU/PCMA */
        media_cfg.snd_clock_rate = 0;      /* Let PJSIP auto-detect */
        media_cfg.ec_tail_len = 200;
        media_cfg.quality = 8;
        media_cfg.no_vad = 1;              /* Disable VAD for clarity */

        status = pjsua_init(&cfg, &log_cfg, &media_cfg);
        if (status != PJ_SUCCESS) {
            pjsua_destroy();
            return (int)status;
        }
    }

    /* Add UDP transport */
    {
        pjsua_transport_config tcfg;
        pjsua_transport_config_default(&tcfg);
        tcfg.port = 0; /* Random port */

        status = pjsua_transport_create(PJSIP_TRANSPORT_UDP, &tcfg, &g_transport_id);
        if (status != PJ_SUCCESS) {
            pjsua_destroy();
            return (int)status;
        }
    }

    /* Start pjsua */
    status = pjsua_start();
    if (status != PJ_SUCCESS) {
        pjsua_destroy();
        return (int)status;
    }

    /* ─── Configure sound devices ─── */
    {
        int dev_count = (int)pjmedia_aud_dev_count();
        PJ_LOG(3, ("wrapper", "Audio devices found: %d", dev_count));

        if (dev_count > 0) {
            /* Try to set default sound devices */
            status = pjsua_set_snd_dev(PJMEDIA_AUD_DEFAULT_CAPTURE_DEV,
                                        PJMEDIA_AUD_DEFAULT_PLAYBACK_DEV);
            if (status != PJ_SUCCESS) {
                PJ_LOG(2, ("wrapper", "Default audio dev failed (%d), trying null audio", status));
                /* Fallback: use null sound device so calls still work */
                status = pjsua_set_null_snd_dev();
                if (status != PJ_SUCCESS) {
                    PJ_LOG(1, ("wrapper", "Null audio also failed: %d", status));
                }
            } else {
                PJ_LOG(3, ("wrapper", "Audio devices configured OK"));
            }
        } else {
            PJ_LOG(2, ("wrapper", "No audio devices, using null audio device"));
            pjsua_set_null_snd_dev();
        }
    }

    g_initialized = PJ_TRUE;
    return 0;
}

EXPORT void __stdcall softphone_destroy(void) {
    if (!g_initialized) return;

    if (g_acc_id != PJSUA_INVALID_ID) {
        pjsua_acc_del(g_acc_id);
        g_acc_id = PJSUA_INVALID_ID;
    }

    pjsua_destroy();
    g_initialized = PJ_FALSE;
    g_server[0] = '\0';
    g_port = 5060;
}

EXPORT int __stdcall softphone_register(const char *server, const char *user,
                                         const char *password, int port) {
    pjsua_acc_config acc_cfg;
    pj_status_t status;
    char id_uri[512];
    char reg_uri[512];

    if (!g_initialized) return -1;

    /* Store server info globally for building call URIs later */
    strncpy(g_server, server, sizeof(g_server) - 1);
    g_server[sizeof(g_server) - 1] = '\0';
    g_port = (port > 0) ? port : 5060;

    /* Remove existing account */
    if (g_acc_id != PJSUA_INVALID_ID) {
        pjsua_acc_del(g_acc_id);
        g_acc_id = PJSUA_INVALID_ID;
    }

    if (port > 0 && port != 5060) {
        snprintf(id_uri, sizeof(id_uri), "sip:%s@%s:%d", user, server, port);
        snprintf(reg_uri, sizeof(reg_uri), "sip:%s:%d", server, port);
    } else {
        snprintf(id_uri, sizeof(id_uri), "sip:%s@%s", user, server);
        snprintf(reg_uri, sizeof(reg_uri), "sip:%s", server);
    }

    PJ_LOG(3, ("wrapper", "Registering: id=%s reg=%s", id_uri, reg_uri));

    pjsua_acc_config_default(&acc_cfg);
    acc_cfg.id = make_pj_str(id_uri);
    acc_cfg.reg_uri = make_pj_str(reg_uri);
    acc_cfg.cred_count = 1;
    acc_cfg.cred_info[0].realm = make_pj_str("*");
    acc_cfg.cred_info[0].scheme = make_pj_str("digest");
    acc_cfg.cred_info[0].username = make_pj_str(user);
    acc_cfg.cred_info[0].data_type = PJSIP_CRED_DATA_PLAIN_PASSWD;
    acc_cfg.cred_info[0].data = make_pj_str(password);

    /* Registration settings */
    acc_cfg.reg_timeout = 300;
    acc_cfg.reg_retry_interval = 30;

    /* Allow contact rewrite for NAT traversal */
    acc_cfg.allow_contact_rewrite = PJ_TRUE;
    acc_cfg.contact_rewrite_method = 2;

    status = pjsua_acc_add(&acc_cfg, PJ_TRUE, &g_acc_id);
    if (status != PJ_SUCCESS) {
        PJ_LOG(1, ("wrapper", "pjsua_acc_add failed: status=%d", status));
    }
    return (int)status;
}

EXPORT void __stdcall softphone_unregister(void) {
    if (g_acc_id != PJSUA_INVALID_ID) {
        pjsua_acc_set_registration(g_acc_id, PJ_FALSE);
    }
}

EXPORT int __stdcall softphone_make_call(const char *dest_uri) {
    pj_str_t uri;
    pjsua_call_id call_id = PJSUA_INVALID_ID;
    pj_status_t status;
    char full_uri[512];
    pjsua_call_setting call_opt;
    pjsua_msg_data msg_data;

    if (!g_initialized || g_acc_id == PJSUA_INVALID_ID) {
        PJ_LOG(1, ("wrapper", "make_call: not initialized or no account"));
        return -1;
    }
    if (!dest_uri || dest_uri[0] == '\0') {
        PJ_LOG(1, ("wrapper", "make_call: empty destination"));
        return -2;
    }

    /* Set up call settings with explicit audio */
    pjsua_call_setting_default(&call_opt);
    call_opt.aud_cnt = 1;
    call_opt.vid_cnt = 0;

    /* Initialize msg_data */
    pjsua_msg_data_init(&msg_data);

    /* Build the SIP URI */
    if (strncmp(dest_uri, "sip:", 4) == 0 || strncmp(dest_uri, "sips:", 5) == 0) {
        /* Already a full SIP URI, use as-is */
        snprintf(full_uri, sizeof(full_uri), "%s", dest_uri);
    } else {
        /* Build URI from stored server info */
        if (g_server[0] == '\0') {
            PJ_LOG(1, ("wrapper", "make_call: no server stored, register first"));
            return -3;
        }

        if (g_port > 0 && g_port != 5060) {
            snprintf(full_uri, sizeof(full_uri), "sip:%s@%s:%d", dest_uri, g_server, g_port);
        } else {
            snprintf(full_uri, sizeof(full_uri), "sip:%s@%s", dest_uri, g_server);
        }
    }

    uri = make_pj_str(full_uri);

    PJ_LOG(3, ("wrapper", "Making call to: %.*s (acc_id=%d)", (int)uri.slen, uri.ptr, g_acc_id));

    /* Ensure sound device is active before placing call */
    {
        int cap_dev = -1, play_dev = -1;
        pjsua_get_snd_dev(&cap_dev, &play_dev);
        if (cap_dev < 0 || play_dev < 0) {
            PJ_LOG(2, ("wrapper", "No sound device active, configuring now..."));
            status = pjsua_set_snd_dev(PJMEDIA_AUD_DEFAULT_CAPTURE_DEV,
                                        PJMEDIA_AUD_DEFAULT_PLAYBACK_DEV);
            if (status != PJ_SUCCESS) {
                PJ_LOG(2, ("wrapper", "Default snd dev failed (%d), using null", status));
                pjsua_set_null_snd_dev();
            }
        }
    }

    status = pjsua_call_make_call(g_acc_id, &uri, &call_opt, NULL, &msg_data, &call_id);
    if (status != PJ_SUCCESS) {
        char errbuf[256];
        pj_strerror(status, errbuf, sizeof(errbuf));
        PJ_LOG(1, ("wrapper", "pjsua_call_make_call FAILED: status=%d (%s)", status, errbuf));
        return -((int)status);
    }

    PJ_LOG(3, ("wrapper", "Call initiated, call_id=%d", call_id));
    return (int)call_id;
}

EXPORT int __stdcall softphone_answer_call(int call_id, int code) {
    if (!g_initialized) return -1;
    return (int)pjsua_call_answer((pjsua_call_id)call_id, code > 0 ? code : 200, NULL, NULL);
}

EXPORT int __stdcall softphone_hangup_call(int call_id) {
    if (!g_initialized) return -1;
    return (int)pjsua_call_hangup((pjsua_call_id)call_id, 0, NULL, NULL);
}

EXPORT int __stdcall softphone_hangup_all(void) {
    if (!g_initialized) return -1;
    pjsua_call_hangup_all();
    return 0;
}

EXPORT int __stdcall softphone_set_hold(int call_id, int hold) {
    if (!g_initialized) return -1;
    if (hold) {
        return (int)pjsua_call_set_hold((pjsua_call_id)call_id, NULL);
    } else {
        pjsua_call_setting opt;
        pjsua_call_setting_default(&opt);
        opt.flag = PJSUA_CALL_UNHOLD;
        return (int)pjsua_call_reinvite2((pjsua_call_id)call_id, &opt, NULL);
    }
}

EXPORT int __stdcall softphone_set_mute(int call_id, int mute) {
    pjsua_call_info ci;
    pj_status_t status;

    if (!g_initialized) return -1;

    status = pjsua_call_get_info((pjsua_call_id)call_id, &ci);
    if (status != PJ_SUCCESS) return (int)status;

    if (mute) {
        /* Disconnect mic from call */
        pjsua_conf_disconnect(0, ci.conf_slot);
    } else {
        /* Reconnect mic to call */
        pjsua_conf_connect(0, ci.conf_slot);
    }
    return 0;
}

EXPORT int __stdcall softphone_send_dtmf(int call_id, const char *digits) {
    pj_str_t d;
    if (!g_initialized) return -1;
    d = make_pj_str(digits);
    return (int)pjsua_call_dial_dtmf((pjsua_call_id)call_id, &d);
}

EXPORT int __stdcall softphone_set_rx_level(int call_id, float level) {
    pjsua_call_info ci;
    pj_status_t status;

    if (!g_initialized) return -1;
    status = pjsua_call_get_info((pjsua_call_id)call_id, &ci);
    if (status != PJ_SUCCESS) return (int)status;

    return (int)pjsua_conf_adjust_rx_level(ci.conf_slot, level);
}

EXPORT int __stdcall softphone_set_tx_level(int call_id, float level) {
    pjsua_call_info ci;
    pj_status_t status;

    if (!g_initialized) return -1;
    status = pjsua_call_get_info((pjsua_call_id)call_id, &ci);
    if (status != PJ_SUCCESS) return (int)status;

    return (int)pjsua_conf_adjust_tx_level(ci.conf_slot, level);
}

EXPORT int __stdcall softphone_get_call_info(int call_id, int *state, int *media_status,
                                               int *duration_sec) {
    pjsua_call_info ci;
    pj_status_t status;

    if (!g_initialized) return -1;
    status = pjsua_call_get_info((pjsua_call_id)call_id, &ci);
    if (status != PJ_SUCCESS) return (int)status;

    if (state) *state = (int)ci.state;
    if (media_status) *media_status = (int)ci.media_status;
    if (duration_sec) {
        *duration_sec = (int)(ci.connect_duration.sec);
    }
    return 0;
}

EXPORT int __stdcall softphone_get_reg_status(int *is_registered) {
    if (!g_initialized || g_acc_id == PJSUA_INVALID_ID) {
        if (is_registered) *is_registered = 0;
        return -1;
    }

    pjsua_acc_info info;
    pj_status_t status = pjsua_acc_get_info(g_acc_id, &info);
    if (status != PJ_SUCCESS) return (int)status;

    if (is_registered) *is_registered = (info.status == 200) ? 1 : 0;
    return 0;
}

EXPORT int __stdcall softphone_get_sound_device_count(void) {
    if (!g_initialized) return 0;
    return (int)pjmedia_aud_dev_count();
}

EXPORT int __stdcall softphone_get_sound_device_name(int index, char *name_buf, int buf_len) {
    pjmedia_aud_dev_info info;
    pj_status_t status;

    if (!g_initialized) return -1;
    status = pjmedia_aud_dev_get_info(index, &info);
    if (status != PJ_SUCCESS) return (int)status;

    strncpy(name_buf, info.name, buf_len - 1);
    name_buf[buf_len - 1] = '\0';
    return info.input_count > 0 ? 1 : 2; /* 1=input, 2=output */
}

EXPORT int __stdcall softphone_set_sound_devices(int capture_dev, int playback_dev) {
    if (!g_initialized) return -1;
    return (int)pjsua_set_snd_dev(capture_dev, playback_dev);
}

EXPORT int __stdcall softphone_transfer_call(int call_id, const char *dest_uri) {
    pj_str_t uri;
    if (!g_initialized) return -1;
    uri = make_pj_str(dest_uri);
    return (int)pjsua_call_xfer((pjsua_call_id)call_id, &uri, NULL);
}
