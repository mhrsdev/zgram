# Telegram Desktop UI design reference (for a Flutter re-creation)

Source studied (read-only): the shallow public clone at `/home/user/telegramdesktop/tdesktop`.
All paths below are relative to that clone root. All lengths are device-independent "px" as written in the
`.style` files (Telegram Desktop's own DSL; 1 px here = 1 logical pixel = 1 Flutter dp at 100% UI scale).
Everything is paraphrased and summarised; no source is reproduced.

**Important caveat about the clone.** The base UI library (`Telegram/lib_ui`) is a git submodule and is
empty in this clone. The files that live there (`ui/colors.palette`, `ui/basic.style`,
`ui/widgets/widgets.style`, `ui/layers/layers.style`, `ui/style/style_core_font.cpp`) were read from the
upstream repo `desktop-app/lib_ui` (master) instead. They are cited as `lib_ui/...` and are the same files
the app builds from (`Telegram/lib_ui` is that repo). Everything else comes from the clone itself.

How styles are named: a `.style` file defines named constants and "struct" instances. `st::foo` in C++ is
`foo` in a `.style` file. Colours are never literal in widget styles; they reference palette names
(`windowBg`, `msgInBg`, ...), so the whole look is re-skinnable. Mirror that in Flutter with a
`ThemeExtension` that holds the palette tokens (section 4).

---

## 1. Overall window layout

### 1.1 Structure
Three logical columns plus an optional far-left rail:

| Column | What | Source |
|---|---|---|
| (optional) Filters rail | vertical folder ("chat filters") bar, 72 px wide, shown only if the user enabled left-side folders and there is room | `Telegram/SourceFiles/window/window.style` (`windowFiltersWidth`), `window/window_filters_menu.cpp`, `window/window_session_controller.cpp` (`filtersWidth`, `enoughSpaceForFilters`) |
| 1. Left: dialogs | top bar (hamburger + search pill), optional stories strip, optional horizontal folder tabs, chat list, bottom bars (update / downloads / load more) | `dialogs/dialogs_widget.cpp` (`updateControlsGeometry`), `dialogs/dialogs_widget.style`, `dialogs/dialogs.style` |
| 2. Centre: chat (or any "section": profile, media, settings in narrow mode) | chat top bar, message list, composer | `history/history_widget.cpp`, `history/view/history_view_top_bar_widget.cpp`, `history/view/history_view_list_widget.cpp` |
| 3. Right: "third section" | Info/profile/shared media panel, or the tabbed emoji/sticker selector pinned as a column | `info/info_wrap_widget.cpp` (wrap `Side`), `window/window_session_controller.cpp` |

The main menu (hamburger drawer, section 6) is a separate 274 px panel that slides over the left column,
not a permanent column (`window/window_main_menu.style` -> `mainMenuWidth`).

### 1.2 Hard numbers (`window/window.style`)
- Window min size 380 x 480; default 800 x 600; "big" default 1024 x 768 (used for the third-column default).
- Left column: min 260, no real max (max 540 is declared but its clamp is commented out in code). Default share = 5/14 of body width (`core/core_settings.h` -> `kDefaultDialogsWidthRatio`; a different "big" ratio is used where third column is on by default, i.e. Mac App Store builds). User can drag the splitter; the ratio is persisted separately for "chat open" and "no chat open".
- Centre column: min 380 (`columnMinimalWidthMain`, also `historyMinimalWidth` in `ui/chat/chat.style`).
- Third column: min 292, max 392 (`columnMinimalWidthThird`, `columnMaximalWidthThird`), stored width clamped into that range.
- "Wide chat" threshold: 880 px of centre-column width (`adaptiveChatWideWidth`). Below it the chat is "Narrow" mode (own messages right-aligned with bubbles of limited width); above it "Wide" mode (see 3.2). Checked in `history/history_widget.cpp` and `history/view/history_view_chat_section.cpp`.

### 1.3 Breakpoints / collapse logic
Computed in `window/window_session_controller.cpp` (`computeColumnLayout`) and `window/window_adaptive.cpp`
(layout enum OneColumn / Normal / ThreeColumn). Let `body = window width - filters rail width (0 or 72)`.

1. `body < 260 + 380 = 640` -> **OneColumn**: only dialogs OR only the chat/section is visible, navigation is push/slide (back arrow in the chat top bar, 240 ms slide, `slideDuration` in `lib_ui/ui/basic.style`). Dialogs takes the full width. `mainwidget.cpp` (`updateWindowAdaptiveLayout`) has a special case: a one-column layout whose chat width would fit Normal is promoted to Normal with the dialogs column pinned at 260.
2. `640 <= body < 260 + 380 + 292 = 932` (or the user has no third section enabled) -> **Normal** (two columns). Dialogs = body x ratio, at least 260, and never so large that the chat drops below 380.
3. `body >= 932` and the third section (info or tabbed emoji panel) is enabled -> **ThreeColumn**. Dialogs width from ratio, third from saved width (292..392); if the sum leaves less than 380 for the chat, dialogs and third are shrunk proportionally, then clamped to their minimums (`shrinkDialogsAndThirdColumns`). Opening the third section can also try to *grow the OS window* by the third column's width instead of squeezing (`resizeForThirdSection`); closing gives the width back.
4. Info panel as a layer: when there is no room for a third column the profile opens as a centred overlay ("Layer" wrap) with width `min(parent - 2*48, 392)`, minimum 324 (`info/info.style`: `infoMinimalWidth` 324, `infoDesiredWidth` 392, `infoMinimalLayerMargin` 48; `info/info_layer_widget.cpp`). In OneColumn it replaces the chat column ("Narrow" wrap).
5. The dialogs panel has a compact "narrow" state of width 66 (= 10 + 46 + 10, userpics only, `dialogsSmallColumnWidth()` in `window_session_controller.cpp`). It is used mid-animation when dialogs collapses (the search pill morphs into a search icon, the hamburger centres). Chat rows paint only the avatar when narrower than 260.

Column separation: 1 px vertical shadow line (`mainwidget.cpp` `_sideShadow`, hidden in OneColumn); no gaps or cards. Column swap animations use `slideDuration` 240 ms (`lib_ui/ui/basic.style`) and `dialogsWidthDuration` 120 ms (`universalDuration`).

### 1.4 Left column anatomy (top to bottom)
- Search/controls row: height = `topBarHeight` 54. Hamburger button 40 x 40 (`dialogsMenuToggle`, icon `icons/dialogs/dialogs_menu`), at x = 7 (`dialogsFilterPadding` is point(7,7)). Search field to its right: height 35, fully rounded pill (corner radius 18), fill `filterInputInactiveBg` (= `windowBgOver`) idle and `filterInputActiveBg` (= `windowBg`) with a 2 px `windowBgRipple` ring when focused; placeholder "Search"; text margins 12/8/30/5 (`dialogsFilter` in `dialogs/dialogs.style`). Right-edge extras inside search state: cancel (cross, 35 x 35), calendar/jump-to-date (32 x 35), "from user" filter (29 x 35). A lock icon (36 x 38) appears if a passcode is set. The story avatars (see below) sit at the right end of this row when collapsed.
- Stories strip (optional): collapsed = small overlapped avatars (21 px photos, 35 high) in the search row; expanded = a 77 px high row of 42 px avatars with 4 px ring (2 px gap for read stories, opacity 0.6) (`dialogsStories`, `dialogsStoriesFull` in `dialogs.style`).
- Folder tabs (optional, horizontal): `chatsFiltersTabs` = a `SettingsSlider` 33 px high, label semibold 13 px (`semiboldTextStyle`), idle colour `windowSubTextFg`, active `lightButtonFg`, indicator is a 6 px stroke with 2 px radius under the label, 9 px side padding, 18 px min label gap, indicator and tab ripple animate 150 ms (`dialogs/dialogs.style` -> `dialogsSearchTabs`, `chatsFiltersTabs`; widget `ui/widgets/chat_filters_tabs_slider.cpp`). Tabs scroll horizontally with a hidden scrollbar (`dialogsTabsScroll`). Tab can show an icon (5 px skip) plus text, icon-only, or text-only (`ChatsFiltersTabsMode`).
- Vertical filters rail alternative (72 px wide): each button min-height 62, icon at top, 11 px semibold caption under it (text top 40), unread badge top-right (17 high, 12 px semibold text, 2 px stroke in rail colour). Rail colours: `sideBarBg`, `sideBarBgActive`, `sideBarTextFg`, `sideBarIconFg` (+ `*Active`, `sideBarBadgeBg`, `sideBarBadgeBgMuted`). Day theme intentionally uses a dark rail (#293a4c) (`window/window.style` `windowFiltersButton`, `lib_ui/ui/colors.palette` lines ~645-651).
- Chat list: virtual scroll list of rows (section 2). Hover row = `dialogsBgOver`; selected chat = `dialogsBgActive` with inverted (white) text.
- Bottom bars stacked at bottom: "Update Telegram" button (46 high), downloads bar, "Show more chats" (36 high) (`dialogsUpdateButton`, `dialogsLoadMoreButton`).
- Archive: pinned entry at the top of the list that looks like a normal row (grey-blue circular userpic with archive icon `icons/archive_userpic.png`, name coloured `dialogsArchiveFg`); opening it replaces the list with an in-column sub-view and back arrow (`_openedFolder`). Forums (topic groups) open the same way, with topic rows (`forumDialogRow`).
- Chat list swipe (touch/trackpad) actions exist (`dialogs/dialogs_quick_action*.cpp`), not needed for desktop parity.

---

## 2. Dialogs row anatomy

All from `dialogs/dialogs.style` (`defaultDialogRow` etc.) and painted in `dialogs/ui/dialogs_layout.cpp`.

### 2.1 Default row (62 px high)
- Row height 62; padding L10 T8 R10 B8.
- Userpic: circle, **46 px**, at (10, 8). Shape is a circle for users/channels/groups; forum groups use a rounded square (radius is a fraction of size, see `ui/userpic` / `Ui::PeerUserpicView`, not in .style).
- Name: x = 68, baseline box top y = 10, font **13 px semibold** (`semiboldFont`, family Open Sans), colour `dialogsNameFg`; 1 line, ellipsised. Left of the name: a type icon for groups/channels/bots/forums (`dialogsChatIcon`, `dialogsChannelIcon`, `dialogsBotIcon`, `dialogsForumIcon`, drawn in the name colour with a 3 px gap `dialogsChatTypeSkip`). Right of the name (in this order): verified badge (20 px star+check, `dialogsVerifiedIcon`), premium star / emoji status, mute icon (`dialogsMuteIcon`, `icons/dialogs/dialogs_mute.png`, colour `dialogsUnreadBgMuted`, 4 px gap).
- Time (top right): **13 px regular** (`dialogsDateFont`), colour `dialogsDateFg`, right aligned to the row's right padding, baseline aligned with the name; 5 px gap between name and time (`dialogsDateSkip`). Format: today = `H:mm`, this week = weekday short, older = `d.MM.yy`.
- Send-state ticks for your own last message: sent (single) / received (double) / sending (clock) icons left of the time, colour `dialogsSentIconFg` (day #5dc452 green, night #72bcfd blue); `dialogsSendingIconFg` for the clock (`dialogsSentIcon`, `dialogsReceivedIcon`, `dialogsSendingIcon`; 20 px reserved `dialogsSendStateSkip`).
- Preview text: x = 68, y = 34, **13 px regular** (`dialogsTextFont` = `normalFont`), colour `dialogsTextFg`; up to 1 line, elided. Prefix label for groups: sender name (or "You:") in `dialogsTextFgService` (accent blue); drafts show "Draft:" in `dialogsDraftFg` (red, day #dd4b39 / night #ff525d); media shows a coloured type word ("Photo", "Voice message"...) in service colour, optionally with a 16 px rounded-2px mini thumbnail of the media in front of the text (`dialogsMiniPreview` 16, radius 2, skip 2). Forwarded / reply mini-icons exist (`dialogsMiniForward`). Emoji render inline at 18 px (`emojiSize`).
- Typing indicator: replaces the preview line with "X is typing" (or "typing", "recording voice message...", "sending photo" ...) in `dialogsTextFgService` accent colour plus a small animated dots/wave glyph before it (`SendActionAnimation`, rect via `RowPainter::SendActionAnimationRect` in `dialogs_layout.cpp`; animation in `history/view/history_view_send_action.cpp`). Same widget is used in the chat header.
- Right-side badges on the preview line, right-aligned, painted right-to-left with a 5 px gap (`dialogsUnreadPadding`): pinned icon (`dialogsPinnedIcon`, colour `dialogsUnreadBgMuted`), unread counter, mention "@" badge, reaction heart badge, poll badge.
- Unread counter: pill, height **19** (`dialogsUnreadHeight`), min width 19 (circle for 1 digit), horizontal padding 5, text **12 px bold** white (`dialogsUnreadFont`, `dialogsUnreadFg`); fill `dialogsUnreadBg` (day #40a7e3, night #4082bc) for unmuted chats, `dialogsUnreadBgMuted` (day #bbbbbb, night #3e546a) for muted. On a selected row the pill inverts (white fill, `dialogsBgActive` text). Counts above 999 are abbreviated ("1K").
- Unread-without-count (marked unread) = same pill but empty (just a dot-like 19 px circle) (`PaintUnreadBadge` path in `dialogs_layout.cpp`).
- Online badge: 10 px green dot with 2 px ring in the row bg, at the userpic's bottom-right (offset 0,2) (`dialogsOnlineBadgeSize/Stroke/Skip`, colour `dialogsOnlineBadgeFg` #4dc920 day / #63b6ff night), fades in 150 ms. Other userpic badges (16 px): ongoing group call `dialogsCallBadgeSize`, subscription, community. A 20 px auto-delete (TTL) badge exists for secret-ish chats.
- Story ring: when a user has an active story the userpic gets a gradient ring (see `dialogsStories`, `ui/dialogs_stories_list.cpp`).
- Scam/fake label: tiny 9 px semibold text in a 2 px radius box (`dialogsScamFont`).

### 2.2 Row variants
- With folder tag chips under the preview ("tagged"): height 72, text top 30, tag row top 52, tag font 10 px, 4 px gap (`taggedDialogRow`, `dialogRowFilterTagStyle`).
- Forum (topic group): height 80 (text top 32) with a second line listing recent topics (21 px topic icon, bold 11 px number, jump arrow chip radius 11) (`forumDialogRow`, `defaultForumTopicIcon`). Tagged forum: 96.
- Community/"info" variants use left padding 22, text x = 80.
- Search-result rows reuse the same layout (peer rows), with section headers; "search in" chip row is 38 px high with a 28 px userpic (`dialogsSearchInHeight`, `dialogsSearchInPhotoSize`).
- Selected/hover/ripple: ripple colour `dialogsRippleBg` (= `windowBgRipple`), selected-chat ripple `dialogsRippleBgActive` (`dialogsRipple`). Ripple timings: show 650 ms, hide 200 ms (`defaultRippleAnimation` in `lib_ui/ui/widgets/widgets.style`).
- Three colour states for every row part: normal / over (hover) / active (current chat). Each colour token exists in all three (`dialogsNameFg`, `dialogsNameFgOver`, `dialogsNameFgActive`, ...). Reproduce as a small state-based resolver.
- Empty states: lottie sticker (`Resources/animations/no_chats.tgs`, `noresults.tgs`) + grey label + button.

### 2.3 Other list elements
- Pinned chats are simply sorted first; drag to reorder; pin icon in the badge slot.
- Section headers in search results are small grey semibold labels on `windowBgOver`-ish strips (generic `ui/widgets/` section style).
- Folder (archive/contacts) back-bar: `_subsectionTopBar`, same height as the search row (54).

---

## 3. Chat view

### 3.1 Header ("top bar")
`history/view/history_view_top_bar_widget.cpp`, styles in `info/info.style` (`topBar*`).
- Height **54** (`topBarHeight`), background `topBarBg` (= `windowBg`; night #17212b), a 1 px bottom shadow separates it from the list.
- Left: back arrow (only in OneColumn or in a sub-view) with left padding such that the avatar/title starts at x = 39 when the arrow is shown (`topBarArrowPadding` 39/8/17/8).
- Avatar button 52 x 54 containing a **42 px** circular userpic at offset (2, -1) (`topBarInfoButton`, community: 34 px). Clicking opens the info panel.
- Title: first line, **13 px semibold** (`msgNameStyle`), colour `dialogsNameFg`, with verified/premium badges after it. Status: second line, **13 px regular**, `historyStatusFg` (= `windowSubTextFg`) e.g. "last seen recently", "online" in `windowActiveTextFg` accent, "typing..." in accent with the animated dots; groups show "N members, M online". The status line baseline sits 8 px above the bar bottom.
- Right-side icon buttons, each 40 x 54 (menu 44 x 54), icons from `Resources/icons/top_bar_*.png`, colour `menuIconFg`/`menuIconFgOver`: group-call (voice chat) icon (groups/channels with a call), call (1:1 users), search, "profile/info" toggle (only if third column is possible, turns accent when open), three-dot menu. Gaps are built into the widths; menu popup appears at offset (-6, 45) from the button.
- Selection mode swaps the header for: "N selected", Forward and Delete buttons, Clear, with a vertical slide of the whole bar (`topBarHeight` slide, ~150 ms).
- Under the header, stackable bars (each ~ 36-49 px): pinned message bar (49 high, left accent line, title in accent + text), "translate" bar, group-call bar, join requests bar, contact-status bar ("Add / Block / Report spam", 49 high). Files: `ui/chat/pinned_bar.cpp`, `history/view/history_view_translate_bar.cpp`, `ui/chat/group_call_bar.cpp`, `history/view/history_view_contact_status.cpp`.
- Connecting state replaces the status with "Connecting..." and a 8 px spinner (`topBarConnectingAnimation`).

### 3.2 Message bubbles
Main styles: `ui/chat/chat.style`, `ui/chat/chat_style.style`; geometry in `history/view/history_view_message.cpp`
(`countGeometry`, `countMessageRounding`), painting in `ui/chat/message_bubble.cpp`.

**Size and padding**
- Max bubble width **430** (`msgMaxWidth`); min width 160 (`msgMinWidth`); the bubble is also capped by available width minus margins.
- Inner text padding L11 T8 R11 B8 (`msgPadding`).
- Outer margin L16 T6 R56 B2 (`msgMargin`), i.e. 6 px above and 2 px below a bubble; attached bubbles (same-sender run) use 0 top margin (`msgMarginTopAttached`). Extra 2 px bottom "shadow" under each bubble (`msgShadow`, colour `msgInShadow` / `msgOutShadow`, day only; transparent in the night theme).
- Group-chat sender avatar: **33 px** circle (`msgPhotoSize`), message content shifted right by 40 (`msgPhotoSkip`). The avatar shows only on the *last* message of a run, aligned to the bubble bottom.
- Layout modes (centre width vs 880): **Narrow** (<880): incoming left, outgoing right-aligned (pushed by extra left margin), both with the 430 cap; **Wide** (>=880): everything left aligned, bubbles still capped at 430 (`delegate()->elementChatMode()`, `ElementChatMode` in `history_view_element.h`).
- Text: **13 px** `msgFont` (Open Sans regular), default colours `historyTextInFg`/`historyTextOutFg`. Names in groups: semibold 13, coloured by sender (peer colour index 1-7, tokens `historyPeer{1..8}NameFg`, see palette table). Links `historyLinkInFg/OutFg`. Mono/code `msgInMonoFg/OutMonoFg`. Spoilers animated particles. Large emoji-only messages: 36 px glyphs with no bubble when 1-3 emoji (`largeEmojiSize` 36, `largeEmojiSkip` 4).
- Sticker max 224 px square (`maxStickerSize`), animated emoji 112 (`maxAnimatedEmojiSize`), GIF/video max 320 (`maxGifSize`), round video 240 (`maxVideoMessageSize`), photo/media max 430 and min 100 (`maxMediaSize`, `minPhotoSize`). Stickers/round videos are drawn without a bubble.

**Shape (corner rules)** - `ui/chat/chat_style_radius.cpp`, `history/view/history_view_message.cpp` (`countMessageRounding`)
- Large radius **16** (`bubbleRadiusLarge`), small radius **6** (`bubbleRadiusSmall` = `roundRadiusLarge`).
- Default: all four corners 16. If the message is attached to the previous one (same sender, <15 minutes apart, no date bar/unread bar between, not a service message) the *top* corner on the sender side becomes 6. If attached to the next one, the *bottom* corner on the sender side becomes 6 and no tail is drawn. The last bubble of a run gets a **tail** on the sender side bottom corner (left for incoming, right for outgoing in narrow mode) instead of a rounded corner: a 6 x 10 px sprite (`Resources/icons/bubble_tail.png`, mirrored for the right side, tinted `msgInBg`/`msgOutBg`) that sits outside the bubble rect. No tail when: media with `skipBubbleTail`, bot inline keyboard under it (then bottom corners = 6), comments root, fake "about" bubble.
- Attach rule constant: 900 seconds (`history/view/history_view_element.cpp` `kAttachMessageToPreviousSecondsDelta`; see `computeIsAttachToPrevious` for the other conditions: same author, same topic, not sponsored, not service, markup not hiding...).
- Bubble fills: incoming `msgInBg`, outgoing `msgOutBg`, selected variants `msgInBgSelected/msgOutBgSelected`. Custom chat themes can use gradients on outgoing bubbles (`ui/chat/chat_theme.cpp`).
- Selection mode: a round checkbox appears at the left (`msgSelectionOffset` 30 px shift).

**Inside the bubble, top to bottom**
1. Forward header ("Forwarded from X"): semibold, `msgInServiceFg`/`msgOutServiceFg`, text style `fwdTextStyle` (13 semibold, no underline); original author avatar tiny inline on newer layouts (`fwdTextUserpicPadding`).
2. Sender name (groups, incoming only), semibold 13, peer colour; may be followed by admin/tag badge (rounded label, `msgTagBadgePadding`).
3. Reply quote block: vertical bar **2 x 36** at x = 1 (`msgReplyBarSize/Pos`), left text skip 10, padding L6 T6 R11 B6; name semibold in accent (`msgInReplyBarColor` / `msgOutReplyBarColor`, day-in #37a1de, day-out #5eb854, night-in #429bdb, night-out #65b9f4), reply text 13 regular in normal text colour, 1 line; optional 32 px square thumbnail (`historyReplyPreview`). Newer message "colour index" scheme tints the block: background = accent at **12 %** alpha, bar = accent at 90 %, second/third stripes at 30 %/60 % (`ui/chat/chat_style.h` `kDefaultBgOpacity` etc., `ui/chat/chat_style.cpp`); quote corners radius 5, outline bar 3 px (`historyQuoteStyle`).
4. Media (photo/video/GIF/file/voice/sticker/poll/location/contact/call) - see 3.3.
5. Text body (13 px). Block quotes: 3 px bar, radius 5, padding 10/2/20/2, collapsible with chevron, tinted bg; code blocks: header strip 20 px with language and copy icon, horizontal scroll (`historyTextStyle` blockquote/pre).
6. Link preview (web page card) - see 3.3.
7. Reactions row: pills below the text (left aligned, 4 px between, `reactionInlineBetween`). Each pill: padding L5 T2 R7 B2, emoji **18 px** (`reactionInlineSize`) then count in 12-13 px; fully rounded. Non-chosen fill = accent at 12 % (incoming) / 18 % (outgoing) opacity (`history/view/reactions/history_view_reactions.cpp` `kInNonChosenOpacity`, `kOutNonChosenOpacity`); chosen = solid accent with inverted text. Multiple people reactions show up to 3 overlapped 20 px userpics (7 px shift) instead of a count. Tag-style (saved message tag) reaction has an arrow-ended left shape (radius 6 left, 3 right).
8. Bottom info (time + status) at the bottom-right inside the bubble, in the same line as the last text line when it fits, otherwise on its own line (text reserves ~ 12 px `msgDateSpace` + the info width). Contents, right to left: sent/read tick (`historySentIcon` single check = delivered, `historyReceivedIcon` double check = read, colour `historyOutIconFg` day #57b84c / night #6bbfff; clock while sending, colour `historySendingOutIconFg`), time `H:mm` in **13 px** (`msgDateFont`) colour `msgInDateFg` (day #a0acb6 / night #6d7f8f) or `msgOutDateFg` (day #6db566 / night #7da8d3), "edited" label before time, views count with eye icon (channels), pin icon, silent (mute) icon, replies count. Over media (no caption) the info sits in a pill with `msgDateImgBg` (#00000054) and white text, padding 8 x 2, radius 6 (`msgDateImgPadding`, `dateRadius`).
9. Inline bot keyboard under the bubble: rows of buttons, each height 36, 2 px margins, radius 3 (`msgBotKbButton`), background `msgServiceBg`-style translucent, centred semibold-ish text.
10. Comments bar for channel posts: 40 px high strip inside bubble bottom with overlapped 25 px userpics, "N comments" + chevron (`historyCommentsButtonHeight`).
11. Right-of-bubble floating action for channels/forwards: 30 px circular "share/forward" button (`historyFastShareSize`) appearing on hover, colour `msgServiceBg`, 13 px from bubble, 5 px above bottom.

**Date separators / service messages**
- Date separator ("Today", "March 4"): centred pill, text **13 px semibold** (`msgServiceFont`) white (`msgServiceFg`), background `msgServiceBg` (translucent; day #517c417f, night #213040d5), padding L12 T3 R12 B4, outer margin 10/10/10/2; pill radius = half its height = **10** (`HistoryServiceMsgRadius`). Floating "sticky" date while scrolling (fades in/out, `historyDateFadeDuration` 200, hidden after 1 s `historyScrollDateHideTimeout`).
- Service messages ("X joined", "Group name changed", pinned notices, calls) use the same pill (can wrap to several lines with "inverted" corner joins between lines - see `history_view_service_message.cpp`, `HistoryServiceMsgInvertedRadius`); text mixes semibold names and regular text, links in white underlined-on-hover.
- "Unread messages" bar: full-width strip 32 px high with a thin shadow, centred semibold label (`historyUnreadBarHeight` 32, `historyUnreadBarBg`, margin 8).
- Gift / big service cards: 236 x ~231 card with a 140 px sticker, radius 20 (`msgServiceGiftBox*`).
- Floating "scroll to bottom" button: 52 x 62 touch area, 42 px circle (`historyToDown`), icon `history_down_arrow`, appears when scrolled up by >= 480 px (`historyToDownShownAfter`), fades 150 ms, carries a 22 px unread badge; sibling buttons stack above it for unread mentions / reactions (`historyUnreadMentions`, `historyUnreadReactions`).
- Scrollbar: 12 px wide overlay with 3 px insets, 3 px radius, colours `historyScrollBg*` / `historyScrollBarBg*` (translucent) (`historyScroll`).

### 3.3 Media layouts
- **Photo / video**: no inner padding; image fills the bubble width (bubble shrinks to image width, 100..430). Corner radius follows the bubble rounding (16/6). Video shows a 44 px play/download circle (`historyVideoDownloadSize`) centred, duration chip top-left (pill `msgDateImgBg`), mute icon top-right for autoplay GIF. Caption sits below inside the same bubble with normal padding.
- **Photo grouping (albums)**: up to 10 items, max total width 430, min 100, 4 px gap (`historyGroupWidthMax/Min`, `historyGroupSkip`). Algorithm in `ui/grouped_layout.cpp` (aspect ratios clamped to 0.6667..2.75, then chooses 1-row/2-row/3-row layouts by comparing candidate rows, "attributes" for sides touching bubble edges to apply outer corners only). Outer corners = bubble rounding; inner corners = small radius. Caption belongs to the whole album.
- **Voice message**: left 44 px circular play button (colour `msgFileInBg` day #40a7e3 / `msgFileOutBg` day #5fbe67), waveform of 2 px wide bars, 1 px gap, height 3..17 px (`msgWaveformBar/Skip/Min/Max`), played portion `msgWaveformInActive/OutActive`, rest `msgWaveformInInactive/OutInactive`; duration under the waveform in small text (date colour); unread dot 7 px beside it (`mediaUnreadSize`); "transcribe" button 28 x 22 to the right for Premium. Recording blob animations in the composer use blobs of radius 25-29 (`msgVoicePlayback*Blob*`). Round-video messages are 240 px circles with a progress ring.
- **File**: layout `msgFileLayout`: padding 12/8/10/8, 44 px circular icon (download arrow / pause / play / cancel as radial progress, 3 px line), name at top 12 semibold-ish 13, status line at top 34 (size, "ETA") in date colour; min width 268. With thumbnail: `msgFileThumbLayout` 72 px square thumbnail (radius 12, small 4), name top 11, status top 31, link row "Open with / Download" at 55 in accent. Colour-coded generic file icons use `msgFile1..4Bg` (blue #72b1df, green #5fbe67, red #e47272, yellow #efc274 day).
- **Link preview card**: appended below the message text; left vertical accent bar (3 px, same quote scheme with 12 % tinted bg), site name in accent semibold, title semibold 13, description regular 13 (max ~ 3 lines), optional large image below (full bubble width, rounded) or 36-ish px square thumbnail on the right, "View post" style button 36 high with 1 px outline (`historyPageButtonHeight`), enlarge button 36 px on image (`webPage*` and `historyPage*` in `chat.style`, `history/view/history_view_webpage_preview.cpp`, `history/view/media/history_view_web_page.cpp`).
- **Poll**, **contact card**, **location map** (map tile with pin), **call record** (240 x 56 with arrow icon), **dice**, **gift**, **invoice**, **story mention** exist in `history/view/media/`. The Flutter port will likely skip most of them in v1.

### 3.4 Composer (message input area)
Styles in `chat_helpers/chat_helpers.style` (`historyCompose*`, `historyAttach`, `historySend*`), logic in `history/history_widget.cpp` and `history/view/controls/`.
- Background `historyComposeAreaBg` (= `msgInBg`: white day / #17212b night... note night value is #17212b), top 1 px shadow. Row height min **46** (buttons 46 high); input grows up to 224 px (`historyComposeFieldMaxHeight`; field style `heightMin` 36, scrolls after that, 6 px fade `historyComposeFieldFadeHeight`).
- Left to right: **Attach** (paperclip, 44 x 46, `icons/chat/input_attach.png`), the **text field** (13 px, placeholder "Write a message...", no border, `placeholderFg`), then right side: optional bot-command "/" button, bot-keyboard toggle, scheduled-messages clock (with red dot), silent-broadcast bell (channels), **emoji** smiley button (44 x 46; shows an animated circle line while the panel is open, `historyAttachEmoji`), and finally **Send / Voice** button.
- Send button: 44 x 46 icon button, paper-plane `chat/input_send.png` coloured `historySendIconFg` (= accent `windowBgActive`). When the field is empty the same slot shows a **microphone** (`input_record.png`) that records on press-hold / click (voice) and long-press toggles to **video message** mode (camera icon). Press-and-hold shows the record bar: red pulsing dot + timer, "Slide to cancel" hint, a lock affordance ("RecordBarLock") that slides up to lock recording, and the blob around the mic (`history/view/controls/history_view_voice_record_bar.cpp/.style`). Right-click / long-press on Send opens "Send without sound / Schedule message". When editing, send is replaced by a check/save icon (`input_save`). Stars-priced send for paid messages shows a pill button.
- **Reply / Edit / Forward bar** above the field: height **49**, background `historyReplyBg`, left 53 px gutter holding a 7,7 offset icon (reply arrow `chat/input_reply_settings`, edit pencil `input_edit`, forward `input_forward`; colour `historyReplyIconFg` accent), then a 2-line `MessageBar`: title semibold 13 in `windowActiveTextFg` ("Reply to Name", "Edit message", "Forward N messages"), body 13 regular in `historyComposeAreaFg`, optional 32 px media thumbnail; right-side 49 x 49 close (X) button; 160 ms cross-fade when content changes (`defaultMessageBar` in `chat_helpers.style`). Tapping the bar scrolls to the target message.
- Link-preview-settings chip appears in the same slot when a URL is typed.
- **Attach menu**: popup with icon rows (Photo or video, File, Poll, Contact, Location, bot web apps...), or a drag-and-drop overlay for files that asks "Drop files here to send / quick vs as file" (`history/history_drag_area.cpp`). After choosing files a **send-files box** (modal) shows previews, a caption field, "compress/group" toggles, and a send button (`boxes/send_files_box.cpp`).
- **Emoji / sticker / GIF panel**: popup anchored above the emoji button; width **345**, height min 278, max 640, default 75 % of available height (`emojiPanWidth/MinHeight/MaxHeight/HeightRatio`), 10 px margins, radius 8 (`emojiPanRadius`), fade/slide 200 ms. Tabs: Emoji | Stickers | GIFs (switch bar at bottom, `Tabbed selector`; with the third column enabled it docks to the right column). Emoji grid cells 34 x 32 (`emojiPanArea`), sticker cells min 64 with 11 px padding, section header semibold 13 in `emojiPanHeaderBg` (translucent), category icon strip at the bottom coloured `emojiPanCategories`. Recent / premium / custom emoji sections. Hover = `emojiPanHover` (`windowBgOver`) rounded 3-px rectangle. Files: `chat_helpers/emoji_list_widget.cpp`, `stickers_list_widget.cpp`, `gifs_list_widget.cpp`, `tabbed_selector.cpp`.
- Suggestions: emoji autocomplete (":smile") and @mention / #hashtag / /command popups over the field (`chat_helpers/field_autocomplete.cpp`).
- Join / mute / unblock states: the composer is replaced by a full-width 46-high text button (`historyComposeButton`, text semibold 13 in accent, `historyUnblock` red) e.g. "JOIN CHANNEL", "MUTE", "UNBLOCK".

### 3.5 Context menu (right-click on a message)
`history/view/history_view_context_menu.cpp`. Popup menu style: radius 8, 150 ms fade (`defaultPopupMenu` in `lib_ui/ui/widgets/widgets.style`), 0 outer skip + 5-8 px scroll padding, item padding L54 T8 R17 B8 with a 24 px icon at x = 15 (`menuWithIcons`), text 13 px regular `windowFg`, hover fill `windowBgOver`, separators 1 px with 4 px vertical padding, destructive items (Delete) in `attentionButtonFg` (#d14e4e day / #ec3942 night). Shadow: 5 px blur, 1 px y-offset, 25 % black (`defaultBoxShadow`).
Typical item order (as present; each appears only when applicable): Reaction strip at top (a floating pill of ~7 quick reactions plus an expand chevron, built in `history/view/reactions/`), Reply, (Quote and reply), Edit, Pin / Unpin, Copy text, Copy link / Copy image, Save as / Save GIF / Save to downloads / Show in folder, Forward, Delete, Select, Report, Translate, Send now / Reschedule (scheduled), Add to / Show sticker pack ("Add stickers"), "Seen by / Reacted by N" row with stacked mini userpics (opens a sub-list), "Open in browser", Tag name edit. The key names (`lng_context_*`) are listed in the source file; labels come from `Telegram/Resources/langs/lang.strings`.
Selecting multiple messages shows a "Forward / Delete" bar in the header instead.

---

## 4. Theme system

### 4.1 How colours are defined
- **Palette definition**: `lib_ui/ui/colors.palette` (698 lines, upstream submodule; not in the clone). Each line is `name: value; // comment`. A value is a `#rrggbb[aa]` literal or a reference to another colour (`msgInBg: windowBg`). `a | b` means "a, unless the theme cannot supply it then b" fallback. A code generator turns this into `style_palette.h` (names become `st::name`).
- **Using colours**: `.style` files reference palette names only.
- **Built-in themes** (zip archives despite the `.tdesktop-theme` extension; contain `colors.tdesktop-theme` text + `background.png`): `Telegram/Resources/night.tdesktop-theme` ("Night"), `night-green.tdesktop-theme`, `day-blue.tdesktop-theme` ("Day Blue"), plus `day-custom-base.tdesktop-theme` and `night-custom-base.tdesktop-theme` (plain-text bases the theme editor starts from; the custom-base files override only a few keys). A theme file lists `name: #hex;` or `name: otherName;` lines; anything missing falls back to `colors.palette`. The first ~20 lines are an explanatory comment header (readable after `unzip`).
- Default **Day** = `colors.palette` as is. Default **Night** = the `night.tdesktop-theme` file (the app's "Night mode" toggle loads it; `window/themes/window_theme.cpp`).
- Chat background/wallpaper: separate from the palette. Day default is the tiled/gradient-ish default wallpaper `Telegram/Resources/art/bg_initial.jpg` (480 x 750, average colour approx #dee3e7); night theme ships its own `background.png` inside the zip. User can choose cloud wallpapers (pattern + gradient) and "chat themes" (per-chat emoji-linked themes incl. gradients on bubbles; `ui/chat/chat_theme.cpp`). Service pill colour (`msgServiceBg`) is recomputed from the wallpaper average colour so text over any wallpaper stays readable (`window/themes/window_theme.cpp`, "adjusted" comments in the palette).
- Accent color: Day primary accent is `windowBgActive` #40a7e3 (buttons, active, send icon); text links use the darker `windowActiveTextFg` #168acd.
- Peer colours (name colours, 7 base + extras; both a name colour and an avatar gradient/base) are `historyPeerNNameFg` / `historyPeerNUserpicBg`; newer "colour index" system with `ui/color_indices.style` adds more (peer-colour 0-6 mapped from the server, plus collectible colours).

### 4.2 Main colours

Day = `lib_ui/ui/colors.palette` (default). Night = `Telegram/Resources/night.tdesktop-theme` (extracted). A dash means "not overridden: same as day / same as the token it references".

| Token | Day | Night |
|---|---|---|
| windowBg (app/dialog bg, boxes, menus) | #ffffff | #17212b |
| windowFg (primary text) | #000000 | #f5f5f5 |
| windowBgOver (hover, search pill idle) | #f1f1f1 | #232e3c |
| windowBgRipple | #e5e5e5 | #24303d |
| windowSubTextFg (secondary text) | #999999 | #708499 |
| windowBoldFg (titles, names) | #222222 | #e9e8e8 |
| windowBgActive (primary accent fill) | #40a7e3 | #5288c1 |
| windowFgActive (text on accent) | #ffffff | #ffffff |
| windowActiveTextFg (accent text, links, "online") | #168acd | #6ab3f3 |
| activeButtonBg (filled button) | = windowBgActive (#40a7e3) | #2f6ea5 |
| lightButtonFg (text buttons) | = windowActiveTextFg | - |
| attentionButtonFg (destructive) | #d14e4e | #ec3942 |
| shadowFg | #00000018 | #04080e56 |
| menuBg / menuBgOver | #ffffff / #f1f1f1 | #17212b / #ffffff literally in the sample file (looks odd; the app's popup hover in night mode looks like windowBgOver #232e3c, so use that) |
| menuIconFg | #999999 | #6c7883 |
| inputBorderFg / filterInputInactiveBg | #e0e0e0 / #f1f1f1 | - / #242f3d |
| boxDividerBg | = windowBgOver | - |
| topBarBg | = windowBg | #17212b |
| dialogsBg | = windowBg | = windowBg |
| dialogsBgOver | #f1f1f1 | #202b36 |
| dialogsBgActive (selected chat) | #419fd9 | #2b5278 |
| dialogsNameFg | #222222 | #f5f5f5 |
| dialogsTextFg (preview) | #999999 | #7f91a4 |
| dialogsDateFg | #999999 | #8696a8 |
| dialogsTextFgService (sender label) | #168acd | #73b9f5 |
| dialogsDraftFg | #dd4b39 | #ff525d |
| dialogsUnreadBg (badge) | #40a7e3 | #4082bc |
| dialogsUnreadBgMuted | #bbbbbb | #3e546a |
| dialogsUnreadFg | #ffffff | #ffffff |
| dialogsSentIconFg (ticks) | #5dc452 | #72bcfd |
| dialogsOnlineBadgeFg | #4dc920 | #63b6ff |
| dialogsArchiveFg | #525252 | (name colour) |
| sideBarBg (folders rail) | #293a4c | #0e1621 |
| sideBarBgActive | #17212b | #25303e |
| sideBarTextFg / sideBarIconFg | #8897a6 / #8393a3 | #768c9e / - |
| sideBarTextFgActive / IconFgActive | #64b9fa / #5eb5f7 | #64b9fa / - |
| sideBarBadgeBg / Muted | #5eb5f7 / #8393a3 | #5eb5f7 / #768c9e |
| msgInBg (incoming bubble) | #ffffff | #182533 |
| msgOutBg (outgoing bubble) | #effdde | #2b5278 |
| msgInBgSelected / msgOutBgSelected | #c2dcf2 / #b7dbdb | #2e70a5 / #2e70a5 |
| historyTextInFg / historyTextOutFg | #000000 / #000000 | #f5f5f5 / #e4ecf2 |
| historyLinkInFg / historyLinkOutFg | #168acd / #168acd | #70baf5 / #83caff |
| msgInServiceFg (forward header, names) | #168acd | #71bafa |
| msgOutServiceFg | #45a32d | #90caff |
| msgInDateFg (time) | #a0acb6 | #6d7f8f |
| msgOutDateFg | #6db566 | #7da8d3 |
| historyOutIconFg (own ticks) | #57b84c | #6bbfff |
| historySendingOutIconFg (clock) | #98d292 | (from palette) |
| msgInReplyBarColor | #37a1de | #429bdb |
| msgOutReplyBarColor | #5eb854 | #65b9f4 |
| msgInMonoFg / msgOutMonoFg | #4e7391 / #459866 | #5a8cb7 / #aed1f3 |
| msgInShadow / msgOutShadow | #748ea229 / #3ac3461d | #748ea200 / #00000000 |
| msgServiceBg (date/service pill) | #517c417f | #213040d5 |
| msgServiceFg | #ffffff | #ffffff |
| msgDateImgBg (time over media) | #00000054 | - |
| msgFileInBg / msgFileOutBg (play/download circles) | #40a7e3 / #5fbe67 | #3f96d0 / #4c9ce2 |
| msgWaveformInActive / Inactive | #40a7e3 / #d4dee6 | #549cd7 / #3a4d61 |
| msgWaveformOutActive / Inactive | #5ebd66 / #b3e2b4 | #62b2fd / #4b7fb3 |
| historyComposeAreaBg | = msgInBg (#ffffff) | #17212b |
| historyComposeIconFg | = menuIconFg (#999999) | #6c7883 |
| historySendIconFg | = windowBgActive | = windowBgActive (#5288c1) |
| historyReplyIconFg | = windowBgActive | - |
| historyUnreadBarBg | #fcfbfa | #182433 |
| historyToDownBg | #ffffff | #1d2b3a |
| historyScrollBarBg | #517c417a | #7f84897a |
| mainMenuCoverBg (drawer header) | = dialogsBgActive (#419fd9) | #276899 |
| toastBg | #2c3033e5 | #000000b2 |
| layerBg (modal dim) | #0000007f | - |
| callAnswerBg | #66c95b | #488fc9 |
| historyPeer1..8 NameFg (red, green, yellow, blue, purple, pink, sea, orange) | #c03d33 / #4fad2d / #d09306 / #168acd / #8544d6 / #cd4073 / #2996ad / #ce671b | #fb6169 / #85de85 / - / #65bdf3 / #b48bf2 / #ff5694 / #62d4e3 / #faa357 |
| historyPeer1..8 UserpicBg (avatar bases) | #ff845e / #9ad164 / #e5ca77 / #5caffa / #b694f9 / #ff8aac / #5bcbe3 / #febb5b | same |

Shadow/ripple rules: popup menus 5 px blur, y+1, 25 % black; modal boxes radius 8 (`boxRadius` in `lib_ui/ui/layers/layers.style`), box width 320 (wide 364), box title bar 48 high with 16 px semibold title.

### 4.3 Sizes helper constants (global, `lib_ui/ui/basic.style`)
- `fsize` 13 (base font), `boxFontSize` 14, `roundRadiusLarge` 6, `roundRadiusSmall` 3, `emojiSize` 18 (+1 px padding), `universalDuration` 120 ms, `slideDuration` 240 ms, `slideWrapDuration` 150 ms.

---

## 5. Typography, icons, animation

### 5.1 Fonts
- Family: **Open Sans**, bundled with the app and registered at start (`lib_ui/ui/style/style_core_font.cpp`; weights loaded: regular, semibold, bold, italic variants). System fallbacks differ per OS; CJK/Arabic/Emoji fall back to system fonts; emoji are drawn from a bundled sprite set (`Telegram/Resources/emoji/`, drawn at 18 px) unless "system emoji" is chosen.
- Sizes used in chrome: 13 px regular body / chat text / previews / time (`fsize`); 13 px semibold for names, tab labels, buttons, menu items in the main drawer, titles in chat header; 14 px (`boxFontSize`) in boxes and settings rows; 16 px semibold box titles and profile name (`boxTitleFont`, `infoProfileCover.name`); 17 px semibold in settings cover name; 12 px for subtitles, unread badge text (bold), reply-bar secondary; 11 px for rail captions and profile action labels; 10 px for tag chips and tiny counters; 9 px semibold for "SCAM" tags; 20 px semibold for "reset scale". Headline numbers never exceed ~20 px: this is a dense UI.
- Line height defaults to font height; text uses natural Open Sans metrics (ascent/descent), message line spacing is the font's built-in line height (~ 18-19 px at 13 px). Use `height: 1.3` roughly in Flutter.
- Links underline only on hover (`kLinkUnderlineActive`); semibold link styles disable underline (`fwdTextStyle`).

### 5.2 Icons
- Location: `Telegram/Resources/icons/` (367 files, ~100 distinct icons) as monochrome PNGs at 1x, `@2x`, `@3x` (plus some `.svg`). Subfolders by feature: `chat/` (composer, reply bar, input icons), `dialogs/` (menu hamburger, mute, pin, sent/received ticks, verified/premium badges), `menu/` (231 popup/drawer icons: reply, forward, copy, pin, delete, etc.), `info/` (profile icons), `settings/`, `calls/`, `mediaview/`, `player/`, `emoji/` (category icons), `folders/`, `send_media/`, `statistics/`, `premium/`, `stories/`, `toast/`, `payments/`, `boosts/`, `poll/`, `voice_lock/`; top level has `bubble_tail.png`, `history_*` (ticks, views, down arrow, pin, replies), `top_bar_*` (search/call/profile/group call), `title_*`.
- Icons are authored as **alpha masks** and recoloured at draw time: the `.style` syntax `icon {{ "path", colorToken, point(offset) }}` stacks one or more masks each with its own palette colour (so verified badge = star mask in accent + check mask in white). Therefore for Flutter: convert to SVG or keep PNG masks and tint with `ColorFiltered`/`Icon` using theme tokens; do not bake colours into assets. Sizes are typically 24 x 24 for menu icons and 20-28 for others.
- Lottie stickers/animations: `Telegram/Resources/animations/*.tgs` (gzipped Lottie, ~50 empty-state/onboarding animations) and `animations/chat`, `animations/settings`, `animations/star_reaction`; `Resources/icons/notify_toggle.lottie`. Use `lottie` package for Flutter after un-gzipping `.tgs`.
- Default avatars: coloured circle (base colour from `historyPeerNUserpicBg`, 2-colour vertical gradient on newer clients) with initials in semibold (`historyPeerUserpicFont` = semibold), font size ~ 0.4 x diameter, white. Special avatars: Saved Messages (blue gradient + bookmark), Archive, Replies, Deleted account, hidden author (`dialogs.style` `dialogs*Userpic`, `icons/archive_userpic.png`).

### 5.3 Animations (durations in ms)
Central timings: ripple show 650 / hide 200 (`defaultRippleAnimation`), toggles/checkbox 120 (`universalDuration`), page/column slide 240, wrap show/hide 150, popup fade 150 + show 200 (`defaultPopupMenu`), panel show 200 (`emojiPanShowDuration`), folder tab slide 200 (swipe 140) (`dialogsFilterSlideDuration`), "to bottom" button 150, date float fade 200, item reveal 150 (`itemRevealDuration`), message bar cross-fade 160, online dot 150, input field state 100. Easings used by the engine (`anim::` helpers, frequency in code: `linear` 46, `easeOutCirc` 44, `sineInOut` 39, plus `easeOutBack`/bounce in a few places): default for slides and reveals is `easeOutCirc`; hovers and colour fades are `linear`; loaders use `sineInOut` loops. Particular effects: tail-less bubble send animation (the sent bubble slides up from the composer), reaction fly-up (`reactionFlyUp` 50 px), reaction effect Lotties on the message, spoiler particle shimmer, blob animation on voice record/play and group call, typing dots, "pull to next channel" arc (`historyPullNext*`), emoji big animated (lottie) stickers, swipe-to-reply on a message (`historySwipeIconSkip` 80). Respect "power saving" flags (`settings/settings_power_saving.cpp`) to disable these.

---

## 6. Settings screen and account switcher

### 6.1 Main menu drawer (hamburger)
`window/window_main_menu.cpp`, `window/window_main_menu.style`, `window/window.style`.
- Slides in from the left over the dialogs list, **274 px** wide, full height, dim layer `layerBg` over the rest; closes on outside click/Esc (240 ms slide).
- **Cover** (header), 134 px high, filled with `mainMenuCoverBg` (day #419fd9 / night #276899): 48 px userpic at (24, 20), top-right "night mode" moon toggle, name at (26, 84) and phone/username (or emoji-status prompt) at (26, 103), white text; premium emoji-status icon next to the name. A chevron/arrow at the right of the name toggles the accounts list (`ToggleAccountsButton`), with an unread badge for other accounts (`mainMenuBadgeSize` 18, font 11 bold; `computeUnreadBadge` sums unread of the *other* accounts). Clicking the userpic also toggles accounts. (There was historically a "cloud" saved-messages button; the code path is now commented out.)
- **Accounts list** (collapsible, SlideWrap, shown/hidden state is a persisted setting, auto-collapsed when fewer than 2 accounts): each account is a settings-button row: 26 px userpic (+2 px inset ring) at x about 23, name semibold 13 starting x = 61, active account has a 2 px accent ring (`windowBgActive`) around its avatar, inactive accounts show an unread count badge on the right (`mainMenuAccountSize` 26, `mainMenuAccountLine` 2, `mainMenuAddAccountButton`). Last row "Add Account" with a plus icon (`settingsIconAdd`). Max accounts = 3, or up to 6 with Premium on enough accounts (`main/main_domain.h` `kMaxAccounts`, `kPremiumMaxAccounts`; `Domain::maxAccounts()`). Context menu on a row: open in new window, log out (`settings/sections/settings_information.cpp` `MakeAccountButton`). Ctrl/middle click opens in a new window. The same list appears at the top of Settings > "My Account" (`SetupAccountsWrap`) and in the tray menu (`tray_accounts_menu.cpp`).
- **Menu items** (button rows, height about 40: padding T11 B9, icon at x = 21, text at x = 61, semibold 13 `windowBoldFg`, hover `windowBgOver`): My Profile, (bot "attach menu" shortcuts), separator, New Group, New Channel, Contacts, Calls, Saved Messages, (Wallet when available), Settings, Night Mode (toggle switch on the right, animated). Source order in `MainMenu::setupMenu`.
- Footer: "Telegram Desktop" + version text in small grey, "About" link; pushed to the bottom, min 80 high (`mainMenuFooterHeightMin`).

### 6.2 Settings
Entry: `showSettings` in `window/window_session_controller.cpp`; implemented as an *Info* section of type Settings (`info/info_controller.h` `Type::Settings`), so it uses the same container as profile pages with back arrow, title bar (`infoLayerTopBarHeight` 56) and a scroll area. On wide windows it appears as the centred layer (width min(window - 96, 392), min 324); in one-column mode it replaces the chat column.
- Layout: a vertical stack of sections separated by 12-ish px grey "divider" strips (full-width bands in `boxDividerBg`) and `AddSkip` spacers; every row is a "settings button": 20 px icon at x = 20 (menu icon in `menuIconColor`), label at x = 60, 14 px (`boxTextStyle`), padding T10 B8 R22, optional right-aligned label/value in `windowActiveTextFg`/sub-text, optional toggle (`settingsButton` in `settings/settings.style`; hover `windowBgOver`).
- **Main page** (`settings/sections/settings_main.cpp`), top to bottom:
  1. Cover: 100 px circular photo, name 17 px semibold, phone + @username, QR button, with "Set profile photo" action; accounts list below (see above).
  2. Row group: My Account (profile), Notifications, Privacy and Security, Chat Settings, Folders (if enabled), Advanced, Devices (sessions/calls), Power Saving, Language (shows current language name on the right), Interface scale (slider with percent label, only on platforms that support it, 60 px side padding), Telegram Premium / Stars / currency / Gift Premium (when available), divider, FAQ, Telegram Features, Ask a Question, divider, then red Log Out text button (`settingsAttentionButton`).
  3. Sub-pages (each its own section file in `settings/sections/`): `settings_information` (edit profile: photo, name, bio with char counter, birthday, username, phone, color/emoji status), `settings_notifications` (+ per-type), `settings_privacy_security` (last seen, passcode, 2FA, blocked users, active sessions, auto-delete), `settings_chat` (theme/wallpaper picker with 80 px theme tiles, text size slider, "Chat list" options, stickers & emoji, animation toggles), `settings_folders`, `settings_advanced` (connection type/proxy, auto-download, spell-check, experimental flags), `settings_calls` (devices), `settings_local_storage`, `settings_shortcuts`, `settings_premium`, `settings_business`, `settings_credits`.
- A search field at the top of settings finds rows by keyword (`settings/settings_search.cpp`, `settings_builder.cpp`: each row has keyword synonyms).
- Dialogs ("boxes") for sub-flows (Edit name, Change phone, language list) are modal cards over a dim layer: width 320 (364 wide), radius 8, title 16 semibold, buttons are flat uppercase-free semibold text in accent (cancel left, confirm right) with 8 px radius ripple.

### 6.3 Profile / right info column (shared media)
`info/info.style`, `info/profile/`, `info/media/`.
- Top bar: 56 high (layer) with back/close, title or a collapsing profile header: up to 180 + 56 px tall photo header with big avatar/gradient backdrop, name and status that shrink into the bar on scroll; action buttons row (Message, Mute, Call, Video, Search, More; each 52 px square at 16 % alpha white over the backdrop, 23 px icon, 11 px label) (`infoProfileTopBar*`).
- Body: info rows (phone, username, bio, birthday, links) as 2-line labelled rows with copy-on-click; toggles (notifications); then the **shared media tabs** (Media, Files, Links, Music, Voice, GIFs, Round, Groups in common, Members...) implemented as a sticky tab bar (`SettingsSlider`) above a content area: media is a square grid, min cell 82 px, 2 px gap (`infoMediaMinGridSize`, `infoMediaSkip`); files/links/voice are list rows with 44-px icon, name, and date.

---

## 7. What is hard to replicate in Flutter (and suggested approach)

1. **Pixel-exact text layout in bubbles with the time/status "float"**: Telegram measures the last line to decide if the time fits on it, and shrinks bubble width to text-fit (shrink-to-fit width of multi-line paragraph). Flutter's `Text` cannot report last-line width natively; use `TextPainter.computeLineMetrics()` + custom `RenderBox`, or `Wrap`-trick with a reserved invisible spacer.
2. **Custom rich text engine**: entities, custom emoji (animated, inline at 18 px), spoiler particles, blockquotes with collapse, mono/pre blocks, links hover underline. Needs `TextSpan` with `WidgetSpan` and a dedicated render object for spoilers.
3. **Bubble drawing**: per-corner radii (16/6/tail) plus the 6 x 10 tail merged into the outline, group albums with per-tile outer-corner logic, and optional gradient/wallpaper-aware bubbles. Use `CustomPainter` with a `Path` built from the rounding spec rather than `BoxDecoration`.
4. **Virtualised, bottom-anchored message list** with stable scroll position while history loads in both directions, sticky floating date, jump-to-message with highlight, unread bar logic and read-tracking by viewport. Use `scrollable_positioned_list`/custom `SliverList` with anchor and `center` key.
5. **Wallpaper + service pill colour adaptation** (average colour derived translucent pills; pattern wallpapers with blend modes; per-chat themes). Needs image analysis and `BlendMode` layering.
6. **Lottie/TGS stickers and reactions** at many instances at once (animated emoji in list, effects on reactions, 60 fps): performance management and caching of frames; power-saving gates.
7. **Voice recording UI**: hold-to-record, slide-to-cancel, lock gesture, live waveform + blob, plus waveform bar rendering with seek and speed; platform audio capture/encoding (Opus/OGG) is native work.
8. **Desktop-grade interaction details**: text selection across multiple messages, drag-and-drop file sends, hover action buttons, right-click menus with reaction strip, keyboard navigation, tray/multi-window, window-size driven column re-flow with animated column width (the 66/260 narrow-state morph of the search pill).
9. **Three-state colour system** (normal/over/active) for each row element and the custom palette-generated theme files (importing `.tdesktop-theme` requires a parser for `name: #hex | otherName;` plus fallback chain and `a | b` fallbacks).
10. **Open Sans metrics and CJK/RTL/emoji fallbacks**, since many numbers (62 px rows, 19 px badges, 54 px bars) are tuned to Open Sans at 13 px; use the same font file (Apache 2.0) to keep vertical rhythm.
11. **Typing/send-action animations**, online-dot ring, story rings with segmented arcs, "pull to next channel" arc, and other small procedural effects.
12. **Account switching with multiple simultaneous sessions** (each account has its own data, connection and unread aggregation) - an architecture concern more than UI, but the drawer and tray design assume it.
