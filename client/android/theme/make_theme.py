#!/usr/bin/env python3
"""Builds assets/zerogram.attheme (the "Zerogram Glass" look) from upstream's darkblue.attheme.

Usage: make_theme.py <path to darkblue.attheme> <output .attheme>

1. Every opaque colour of the base theme is pulled toward the navy/violet palette (greys become blue-greys,
   dark backgrounds become deep navy, blue/purple accents are nudged toward the brand hue).
2. The semantic colours below (wallpaper gradient, bubbles, glass bars, text) are then set explicitly.
Colours are written as #AARRGGBB, which Telegram's theme loader accepts.
"""
import colorsys, sys

BRAND_HUE = 228 / 360.0

OVERRIDES = {
    # surfaces
    'windowBackgroundWhite': '#FF070C20', 'windowBackgroundGray': '#FF040816',
    'windowBackgroundWhiteBlackText': '#FFF2F5FF', 'windowBackgroundWhiteGrayText': '#FF8A98C0',
    'windowBackgroundWhiteGrayText2': '#FF8A98C0', 'windowBackgroundWhiteGrayText3': '#FF7683A8',
    'windowBackgroundWhiteGrayText4': '#FF8A98C0', 'windowBackgroundWhiteGrayText5': '#FF8A98C0',
    'windowBackgroundWhiteGrayText6': '#FF8A98C0', 'windowBackgroundWhiteGrayText7': '#FF8A98C0',
    'windowBackgroundWhiteGrayText8': '#FF8A98C0',
    'windowBackgroundWhiteBlueText': '#FF6C93FF', 'windowBackgroundWhiteBlueText2': '#FF6C93FF',
    'windowBackgroundWhiteBlueText3': '#FF6C93FF', 'windowBackgroundWhiteBlueText4': '#FF7A9CFF',
    'windowBackgroundWhiteBlueText5': '#FF7A9CFF', 'windowBackgroundWhiteBlueText6': '#FF7A9CFF',
    'windowBackgroundWhiteBlueText7': '#FF7A9CFF', 'windowBackgroundWhiteBlueHeader': '#FF7A9CFF',
    'windowBackgroundWhiteLinkText': '#FF7FA6FF', 'windowBackgroundWhiteInputField': '#33FFFFFF',
    'windowBackgroundWhiteInputFieldActivated': '#FF6C93FF', 'divider': '#1FFFFFFF', 'listSelectorSDK21': '#1AFFFFFF',
    'dialogBackground': '#FF0C1536', 'dialogBackgroundGray': '#FF070C20', 'dialogTextBlack': '#FFF2F5FF',
    'dialogTextGray': '#FF8A98C0', 'dialogTextGray2': '#FF8A98C0', 'dialogTextGray3': '#FF8A98C0',
    'dialogTextGray4': '#FF8A98C0', 'dialogTextBlue': '#FF7A9CFF', 'dialogTextBlue2': '#FF7A9CFF',
    'dialogTextBlue4': '#FF7A9CFF', 'dialogButton': '#FF7A9CFF', 'dialogScrollGlow': '#FF0C1536',
    # app bar and chat list
    'actionBarDefault': '#FF070C20', 'actionBarDefaultIcon': '#FFFFFFFF', 'actionBarDefaultTitle': '#FFFFFFFF',
    'actionBarDefaultSubtitle': '#B3C6D2FF', 'actionBarDefaultSelector': '#1AFFFFFF',
    'actionBarDefaultSearch': '#FFFFFFFF', 'actionBarDefaultSearchPlaceholder': '#99C6D2FF',
    'chats_name': '#FFF2F5FF', 'chats_nameMessage': '#FF9DB4FF', 'chats_nameMessage_threeLines': '#FF9DB4FF',
    'chats_message': '#FF8A98C0', 'chats_message_threeLines': '#FF8A98C0', 'chats_date': '#FF8A98C0',
    'chats_unreadCounter': '#FF4F7BFF', 'chats_unreadCounterText': '#FFFFFFFF', 'chats_unreadCounterMuted': '#FF3A456E',
    'chats_pinnedIcon': '#FF8A98C0', 'chats_pinnedOverlay': '#14FFFFFF',
    'chats_actionBackground': '#FF5B6BFF', 'chats_actionPressedBackground': '#FF7A5BFF',
    'chats_actionIcon': '#FFFFFFFF', 'chats_sentCheck': '#FF7FC8FF', 'chats_sentReadCheck': '#FF7FC8FF',
    'chats_sentClock': '#FF8A98C0', 'chats_menuBackground': '#FF0A1030',
    # chat wallpaper: four-colour animated gradient, deep navy to violet
    'chat_wallpaper': '#FF060C26', 'chat_wallpaper_gradient_to': '#FF10286E',
    'key_chat_wallpaper_gradient_to2': '#FF2B1C78', 'key_chat_wallpaper_gradient_to3': '#FF081437',
    # bubbles: glassy incoming, blue-violet gradient outgoing
    'chat_inBubble': '#33FFFFFF', 'chat_inBubbleSelected': '#4DFFFFFF', 'chat_inBubbleShadow': '#00000000',
    'chat_outBubble': '#FF3F67FF', 'chat_outBubbleGradient': '#FF7B4DFF', 'chat_outBubbleGradient2': '#FF3F67FF',
    'chat_outBubbleGradient3': '#FF7B4DFF', 'chat_outBubbleGradientAnimated': '0',
    'chat_outBubbleSelected': '#FF5A78FF', 'chat_outBubbleShadow': '#00000000',
    'chat_messageTextIn': '#FFF4F7FF', 'chat_messageTextOut': '#FFFFFFFF',
    'chat_inTimeText': '#99FFFFFF', 'chat_outTimeText': '#B3FFFFFF',
    'chat_inTimeSelectedText': '#B3FFFFFF', 'chat_outTimeSelectedText': '#CCFFFFFF',
    'chat_outSentCheck': '#FFA8E4FF', 'chat_outSentCheckSelected': '#FFA8E4FF',
    'chat_outSentCheckRead': '#FFA8E4FF', 'chat_outSentCheckReadSelected': '#FFA8E4FF',
    'chat_outSentClock': '#B3FFFFFF', 'chat_outViews': '#B3FFFFFF',
    'chat_messageLinkIn': '#FF86B0FF', 'chat_messageLinkOut': '#FFE6ECFF',
    'chat_serviceBackground': '#8010204F', 'chat_serviceBackgroundSelected': '#A010204F',
    'chat_serviceText': '#FFE8EEFF', 'chat_serviceLink': '#FFE8EEFF', 'chat_serviceIcon': '#FFE8EEFF',
    # input bar
    'chat_messagePanelBackground': '#B30A1233', 'chat_messagePanelText': '#FFFFFFFF',
    'chat_messagePanelHint': '#99A8B8E8', 'chat_messagePanelIcons': '#FFB8C6F5',
    'chat_messagePanelSend': '#FF6C8CFF', 'chat_messagePanelShadow': '#00000000',
    'chat_messagePanelCursor': '#FF7A9CFF',
    # liquid-glass chrome
    'glass_defaultIcon': '#FFFFFFFF', 'glass_defaultText': '#FFFFFFFF', 'glass_targetMainTabs': '#FF0B1230',
    'glass_targetMainTopPanel': '#FF0B1230', 'glass_tabSelected': '#FF7A9CFF',
    'glass_tabSelectedText': '#FF7A9CFF', 'glass_tabUnselected': '#B3FFFFFF',
}


def parse(value):
    value = value.strip()
    if value.startswith('#'):
        h = value[1:]
        if len(h) == 6:
            h = 'FF' + h
        return int(h, 16)
    v = int(value)
    return v & 0xFFFFFFFF


def fmt(argb):
    return '#%08X' % argb


def pull_to_brand(argb):
    a = argb >> 24
    if a < 0x20:
        return argb
    r, g, b = (argb >> 16) & 255, (argb >> 8) & 255, argb & 255
    h, l, s = colorsys.rgb_to_hls(r / 255.0, g / 255.0, b / 255.0)
    if l < 0.30:                       # backgrounds -> deep navy
        h, s, l = BRAND_HUE, min(0.62, max(0.42, s)), l * 0.72
    elif s < 0.18:                     # greys -> blue-greys
        h, s = BRAND_HUE - 0.01, 0.22
    elif l >= 0.30 and s >= 0.40 and 190 / 360.0 <= h <= 280 / 360.0:
        h = BRAND_HUE + (h - BRAND_HUE) * 0.4   # blue/purple accents toward the brand hue
    r, g, b = colorsys.hls_to_rgb(h, l, s)
    return (a << 24) | (round(r * 255) << 16) | (round(g * 255) << 8) | round(b * 255)


def main(src, dst):
    lines = []
    seen = set()
    for raw in open(src, encoding='utf-8').read().split('\n'):
        if '=' not in raw or raw.startswith('WPS') or raw.startswith('WLS'):
            continue
        key, value = raw.split('=', 1)
        if key in ('wallpaperFileOffset',):
            continue
        seen.add(key)
        if key in OVERRIDES:
            continue
        try:
            lines.append(f'{key}={fmt(pull_to_brand(parse(value)))}')
        except ValueError:
            continue
    for key, value in OVERRIDES.items():
        lines.append(f'{key}={value}')
    open(dst, 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
    print(f'{len(lines)} colours written to {dst} ({len(OVERRIDES)} explicit)')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
