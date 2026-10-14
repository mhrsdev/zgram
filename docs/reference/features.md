# Telegram Desktop feature inventory (reference study)

Source studied: official Telegram Desktop (`Telegram/SourceFiles`, shallow clone, read-only, GPLv3). Descriptions are paraphrased. Paths are relative to `Telegram/SourceFiles/`. Note: Telegram Desktop has no secret (E2E) chats; its 1:1 E2E is limited to calls (`calls/`, `tde2e/`).

Architecture hints useful for a new client: `data/` = domain model (peers, history, media, folders); `history/` = chat view and composer; `dialogs/` = chat list; `api/` + `apiwrap.cpp` = MTProto request layer; `main/` = account/session; `storage/` = local cache and uploads/downloads; `window/` = window and session controllers; `boxes/` = modal dialogs; `ui/` = widgets.

## 1. Chats and messaging

| Feature | Description | Paths |
|---|---|---|
| Message list view | Virtualized chat history with grouping, date separators, service messages, unread marker, jump-to-unread/mention buttons | `history/history_widget.cpp`, `history/view/history_view_list_widget.cpp`, `history/view/history_view_element.cpp`, `history/view/history_view_corner_buttons.cpp` |
| Compose controls | Input field, attach, emoji/sticker panel toggle, voice/video record, send button, bottom bars | `history/view/controls/history_view_compose_controls.cpp`, `chat_helpers/message_field.cpp` |
| Send text | Plain/formatted text send with entity parsing, character limit indicator | `api/api_sending.cpp`, `api/api_text_entities.cpp`, `history/view/controls/history_view_characters_limit.cpp` |
| Silent send / schedule / when-online | Send without sound, schedule for later, send when recipient online | `menu/menu_send.cpp`, `history/view/history_view_schedule_box.cpp`, `data/components/scheduled_messages.cpp`, `history/view/history_view_scheduled_section.cpp` |
| Send as (channel/anon identity) | Choose which identity posts in a group/channel | `ui/chat/choose_send_as.cpp` |
| Reply | Reply to a message, with quoting and reply to other chat | `history/view/history_view_reply.cpp`, `history/view/history_view_reply_button.cpp`, `data/data_reply_preview.cpp` |
| Quotes | Reply to a selected fragment; quote blocks in text | `history/view/history_view_reply.cpp`, `history/history_item_components.cpp` |
| Edit message | Edit text/caption/media of sent messages | `history/history_item_edition.cpp`, `boxes/edit_caption_box.cpp`, `api/api_editing.cpp` |
| Delete messages | Delete for me / everyone, bulk delete | `boxes/delete_messages_box.cpp` |
| Moderate (admin) | Delete+ban+report user's messages, anti-spam | `boxes/moderate_messages_box.cpp`, `menu/menu_antispam_validator.cpp` |
| Forward | Forward with/without author, caption change, hide sender, forward to many chats | `boxes/share_box.cpp`, `ui/chat/forward_options_box.cpp`, `history/view/controls/history_view_forward_panel.cpp` |
| Pin messages | Pin/unpin (both sides, silent), pinned bar and pinned list | `boxes/pin_messages_box.cpp`, `history/view/history_view_pinned_bar.cpp`, `history/view/history_view_pinned_section.cpp`, `ui/chat/pinned_bar.cpp` |
| Drafts | Per-chat/topic cloud-synced drafts including reply/link-preview options | `data/data_drafts.cpp`, `history/view/controls/history_view_draft_options.cpp` |
| Compose stash | Park a message draft for later in the composer | `data/data_compose_stash.h`, `history/view/controls/history_view_compose_stash.cpp` |
| Saved Messages | Self-chat as cloud notes, with tags and sublists by source chat | `data/data_saved_messages.cpp`, `data/data_saved_sublist.cpp`, `info/saved/` |
| Saved music | Per-user list of saved audio tracks | `data/data_saved_music.cpp` |
| Message translation | Per-message and whole-chat translate bar with language skip lists | `boxes/translate_box.cpp`, `history/history_translation.cpp`, `history/view/history_view_translate_bar.cpp`, `history/view/history_view_translate_tracker.cpp` |
| Voice transcription | Speech-to-text for voice/video messages | `api/api_transcribes.cpp`, `history/view/history_view_transcribe_button.cpp` |
| AI compose | Rewrite/translate/tone helper inside the composer | `boxes/compose_ai_box.cpp`, `api/api_compose_with_ai.cpp`, `data/data_ai_compose_tones.cpp` |
| Polls and quizzes | Create/vote/close polls, quizzes, anonymous/multi-choice, add options | `boxes/create_poll_box.cpp`, `data/data_poll.cpp`, `history/view/media/history_view_poll.cpp`, `api/api_polls.cpp`, `poll/`, `info/polls/` |
| Todo (checklist) messages | Shared checklists with items others can tick/add | `boxes/edit_todo_list_box.cpp`, `data/data_todo_list.cpp`, `history/view/media/history_view_todo_list.cpp`, `api/api_todo_lists.cpp` |
| Link previews | Web page preview in composer and in messages, size/position options, per-message removal | `history/view/history_view_webpage_preview.cpp`, `history/view/controls/history_view_webpage_processor.cpp`, `history/view/media/history_view_web_page.cpp`, `data/data_web_page.cpp` |
| Instant View | In-app article reader (rich pages, search, zoom) | `iv/` |
| Text formatting | Bold/italic/underline/strike/spoiler/code/pre/quote/links/custom emoji entities, markdown-style shortcuts, rich paste | `chat_helpers/message_field.cpp`, `chat_helpers/rich_paste_toast.cpp`, `api/api_text_entities.cpp` |
| Mentions and hashtags | @user, #hashtag, /command and emoji autocomplete | `chat_helpers/field_autocomplete.cpp`, `chat_helpers/bot_command.cpp`, `chat_helpers/emoji_suggestions_widget.cpp` |
| Spellcheck | Dictionaries manager and inline spell checking | `chat_helpers/spellchecker_common.cpp`, `boxes/dictionaries_manager.cpp` |
| Search in chat | In-chat search with from-user filter, date jump, calendar | `data/data_search_controller.cpp`, `data/data_search_calendar.cpp`, `api/api_messages_search.cpp`, `history/view/controls/history_view_compose_search.cpp` |
| Global search | Search chats, contacts, messages across chats, hashtag posts, recent searches | `dialogs/dialogs_widget.cpp`, `dialogs/dialogs_search_from_controllers.cpp`, `dialogs/dialogs_search_posts.cpp`, `settings/settings_recent_searches.cpp`, `api/api_messages_search_merged.cpp` |
| Read state and receipts | Read marks, read-metrics, who-read lists in groups | `history/view/history_view_read_metrics_tracker.cpp`, `api/api_read_metrics.cpp`, `history/history_unread_things.cpp` |
| Typing / send actions | Typing, recording, uploading indicators | `history/view/history_view_send_action.cpp`, `data/data_send_action.cpp`, `api/api_send_progress.cpp` |
| Self-destruct (TTL) | Auto-delete timer per chat and default; view-once media | `menu/menu_ttl.cpp`, `settings/sections/settings_global_ttl.cpp`, `history/view/controls/history_view_ttl_button.cpp`, `chat_helpers/ttl_media_layer_widget.cpp` |
| Report / spam | Report messages or chats | `boxes/report_messages_box.cpp`, `api/api_report.cpp` |
| Context menus and quick actions | Message context menu, swipe-to-reply, quick action on double click | `history/view/history_view_context_menu.cpp`, `history/view/history_view_quick_action.cpp` |
| Streamed drafts / suggested posts | Bot streaming drafts; paid suggested posts to channels | `history/history_streamed_drafts.cpp`, `api/api_suggest_post.cpp`, `history/view/controls/history_view_suggest_options.cpp` |
| Business chat tools | Away/greeting messages, quick replies, working hours, chatbots, chat links, intro | `settings/business/`, `data/business/`, `history/view/history_view_welcome_messages_section.cpp` |
| Contacts | Add/edit contacts, import, birthdays, top peers | `boxes/add_contact_box.cpp`, `boxes/peers/edit_contact_box.cpp`, `data/data_birthday.cpp`, `data/components/top_peers.cpp` |

## 2. Media

| Feature | Description | Paths |
|---|---|---|
| Attach and send files | Drag/drop, paste, multi-file dialog, compress vs original, captions, spoiler, grouping into albums | `boxes/send_files_box.cpp`, `storage/storage_media_prepare.cpp`, `storage/localimageloader.cpp`, `history/history_drag_area.cpp` |
| Albums (grouped media) | Several photos/videos displayed as one grouped message | `history/view/media/history_view_media_grouped.cpp`, `data/data_groups.cpp` |
| Photo editor | Crop, rotate, draw, text, stickers on photos before send | `editor/` |
| Photos and video viewing | Full-screen viewer with navigation, zoom, rotate, save | `media/view/`, `data/data_photo.cpp`, `data/data_media_rotation.cpp` |
| Video playback | Streaming playback, mini-player, pip, speed, quality | `media/streaming/`, `media/view/`, `media/media_video_frames.cpp` |
| Voice messages | Record (hold/lock), waveform, trim/edit, playback | `history/view/controls/history_view_voice_record_bar.cpp`, `media/audio/media_audio_capture.cpp`, `media/audio/media_audio_edit.cpp`, `media/media_audio_waveform.cpp` |
| Round video messages | Record and play circular video notes | `history/view/controls/history_view_voice_record_button.cpp`, `media/clip/`, `media/media_video_encode.cpp` |
| Files / documents | Generic file bubbles, upload/download with progress, resume | `history/view/media/history_view_document.cpp`, `history/view/media/history_view_file.cpp`, `storage/file_upload.cpp`, `storage/file_download.cpp`, `storage/streamed_file_downloader.cpp` |
| Auto-download rules | Per-chat-type and per-media-kind size/auto-download policy | `boxes/auto_download_box.cpp`, `data/data_auto_download.cpp` |
| Download manager | Downloads list with pause/open/cancel | `data/data_download_manager.cpp`, `info/downloads/` |
| Shared media tabs | Per-chat media, files, links, voice, music, GIFs galleries | `info/media/`, `overview/`, `data/data_shared_media.cpp`, `storage/storage_shared_media.cpp` |
| Global media search | Search media/files across all chats | `info/global_media/` |
| Music player | Audio player with playlist, repeat/shuffle, speed, floating widget, system media keys | `media/player/`, `media/system_media_controls_manager.cpp` |
| Stickers | Sticker sets, installed/featured/archived, favorites, recent, sticker set preview, pack creator | `chat_helpers/stickers_list_widget.cpp`, `boxes/stickers_box.cpp`, `boxes/sticker_set_box.cpp`, `boxes/sticker_creator_box.cpp`, `data/stickers/`, `chat_helpers/stickers_lottie.cpp` |
| Animated stickers and effects | Lottie/WebM stickers, message effects, dice and slot machine | `lottie/`, `history/view/media/history_view_sticker.cpp`, `history/view/media/history_view_dice.cpp`, `history/view/media/history_view_slot_machine.cpp` |
| GIFs | Saved GIFs, search, inline GIF results | `chat_helpers/gifs_list_widget.cpp`, `boxes/send_gif_with_caption_box.cpp` |
| Emoji panel | Emoji picker, skin tones, search, emoji sets, large emoji | `chat_helpers/emoji_list_widget.cpp`, `chat_helpers/emoji_keywords.cpp`, `chat_helpers/emoji_sets_manager.cpp`, `history/view/media/history_view_large_emoji.cpp` |
| Custom emoji | Premium animated emoji in text, emoji statuses | `history/view/media/history_view_custom_emoji.cpp`, `data/data_emoji_statuses.cpp`, `menu/menu_emoji_status.cpp` |
| Tabbed panel | Combined emoji/sticker/GIF picker | `chat_helpers/tabbed_selector.cpp`, `chat_helpers/tabbed_panel.cpp`, `chat_helpers/tabbed_section.cpp` |
| Emoji interactions | Full-screen animated reactions to emoji in chat | `chat_helpers/emoji_interactions.cpp`, `history/view/history_view_emoji_interactions.cpp` |
| Reactions | Add/remove reactions, picker strip, reaction lists, tabs by emoji, default reaction, notify settings | `history/view/reactions/`, `data/data_message_reactions.cpp`, `boxes/reactions_settings_box.cpp`, `info/reactions_list/`, `settings/sections/settings_notifications_reactions.cpp`, `api/api_who_reacted.cpp` |
| Paid reactions | Star-paid reactions on channel posts | `payments/payments_reaction_process.cpp`, `history/view/history_view_paid_reaction_toast.cpp` |
| Stories | View/post stories, replies, reactions, stealth mode, reposts | `media/stories/`, `data/data_stories.cpp`, `data/data_story.cpp`, `info/stories/` |
| Locations and live location | Share location, venues, live location | `history/view/media/history_view_location.cpp`, `history/history_location_manager.cpp`, `data/data_location.cpp` |
| Contacts / dice / games | Contact cards, dice, HTML5 games | `history/view/media/history_view_contact.cpp`, `data/data_game.cpp`, `history/view/media/history_view_game.cpp` |
| Wallpaper / theme files in chat | Shareable wallpaper and theme messages | `history/view/media/history_view_theme_document.cpp`, `data/data_wall_paper.cpp` |
| Ringtones | Custom notification tones upload | `boxes/ringtones_box.cpp`, `api/api_ringtones.cpp` |
| Media spoilers / protected content | Blur spoiler media, no-forward restrictions | `history/view/media/history_view_media_spoiler.cpp`, `history/view/media/history_view_no_forwards_request.cpp` |

## 3. Chat types

| Feature | Description | Paths |
|---|---|---|
| Private chats | 1:1 chats with user profile, common groups, block/unblock | `data/data_user.cpp`, `info/profile/`, `info/common_groups/`, `api/api_blocked_peers.cpp`, `settings/sections/settings_blocked_peers.cpp` |
| Basic groups | Legacy small groups, upgrade to supergroup | `data/data_chat.cpp`, `boxes/peers/edit_peer_info_box.cpp` |
| Supergroups | Large groups with admin roles, permissions, slow mode, history visibility, members visibility | `data/data_channel.cpp`, `boxes/peers/edit_peer_permissions_box.cpp`, `boxes/peers/edit_participant_box.cpp`, `boxes/peers/edit_participants_box.cpp`, `boxes/peers/edit_peer_history_visibility_box.cpp`, `boxes/peers/edit_members_visible.cpp` |
| Channels | Broadcast channels, signatures, reactions, discussion link, post stats | `data/data_channel.cpp`, `boxes/peers/edit_discussion_link_box.cpp`, `boxes/peers/edit_peer_reactions.cpp`, `info/channel_statistics/`, `statistics/` |
| Comments / discussion threads | Threaded replies under channel posts | `data/data_replies_list.cpp`, `history/view/history_view_chat_section.cpp` |
| Forum topics | Topics in supergroups, topic icons/colors, topics-as-tabs, create/edit/close | `data/data_forum.cpp`, `data/data_forum_topic.cpp`, `data/data_forum_icons.cpp`, `boxes/peers/edit_forum_topic_box.cpp`, `boxes/peers/toggle_topics_box.cpp`, `history/view/history_view_subsection_tabs.cpp` |
| Communities | Grouping of chats into a community with pending requests | `data/data_community.cpp`, `boxes/peers/community_box.cpp`, `boxes/peers/manage_community_box.cpp`, `info/community/`, `dialogs/dialogs_community_chats_list.cpp` |
| Invite links and join requests | Create/manage invite links, approve requests, QR links | `api/api_invite_links.cpp`, `boxes/peers/edit_peer_invite_link.cpp`, `boxes/peers/edit_peer_invite_links.cpp`, `boxes/peers/edit_peer_requests_box.cpp`, `api/api_chat_invite.cpp`, `info/requests_list/` |
| Admin log | Recent actions log for groups/channels | `history/admin_log/` |
| Ownership and admin tools | Transfer ownership, username management, peer colors, verify peers | `boxes/peers/channel_ownership_transfer.cpp`, `boxes/username_box.cpp`, `boxes/peers/edit_peer_usernames_list.cpp`, `boxes/peers/edit_peer_color_box.cpp`, `boxes/peers/verify_peers_box.cpp` |
| Members and profile | Member lists, shared media, similar channels, peer short info | `info/members/`, `info/profile/`, `info/similar_peers/`, `boxes/peers/peer_short_info_box.cpp` |
| Bots | Bot commands, menu button, reply keyboards, inline keyboards, bot info | `chat_helpers/bot_keyboard.cpp`, `history/history_item_reply_markup.cpp`, `history/view/controls/history_view_bot_menu_button.cpp`, `data/data_peer_bot_commands.cpp`, `info/bot/` |
| Inline bots | `@bot query` inline results (articles, photos, GIFs, etc.) and recent inline bots | `inline_bots/inline_results_widget.cpp`, `inline_bots/inline_bot_result.cpp`, `inline_bots/inline_bot_send_data.cpp`, `data/components/recent_inline_bots.cpp` |
| Web apps / mini apps | Bot web views, attach menu, main mini app, storage, downloads | `inline_bots/bot_attach_web_view.cpp`, `inline_bots/inline_bot_storage.cpp`, `inline_bots/inline_bot_downloads.cpp`, `data/data_bot_app.cpp`, `webview/`, `boxes/url_auth_box.cpp` |
| Managed bots | Create bots managed on behalf of user | `boxes/peers/create_managed_bot_box.cpp` |
| Choose peer requests | Bot requests user to pick a chat/user | `boxes/peers/choose_peer_box.cpp` |
| Secret chats | Not present in Desktop (only E2E calls) | `tde2e/`, `calls/` |
| Support chats | Official support agent tooling | `support/` |
| Sponsored messages | Ads in channels and search | `data/components/sponsored_messages.cpp`, `boxes/about_sponsored_box.cpp`, `menu/menu_sponsored.cpp` |

## 4. Organization

| Feature | Description | Paths |
|---|---|---|
| Chat list | Virtualized dialog list with previews, badges, typing, draft markers, swipe/quick actions | `dialogs/dialogs_widget.cpp`, `dialogs/dialogs_inner_widget.cpp`, `dialogs/dialogs_row.cpp`, `dialogs/dialogs_quick_action.cpp` |
| Chat folders (filters) | Custom folders by include/exclude rules, chat types, links to share folders, folder tags | `data/data_chat_filters.cpp`, `boxes/filters/`, `settings/sections/settings_folders.cpp`, `window/window_filters_menu.cpp`, `api/api_chat_filters.cpp`, `ui/chat/chats_filter_tag.cpp`, `boxes/choose_filter_box.cpp` |
| Archive | Archive folder with auto-archive of unknown senders, hidden/collapsed archive | `data/data_folder.cpp`, `storage/storage_folder_archive.cpp`, `dialogs/ui/` |
| Pinned chats | Pin chats per folder | `dialogs/dialogs_pinned_list.cpp` |
| Mute / notification settings per chat | Mute durations, sound, preview and per-topic exceptions | `menu/menu_mute.cpp`, `data/notify/`, `settings/sections/settings_notifications_type.cpp` |
| Unread counters and mark as read | Unread/mentions/reaction counters, mark unread, mark all read | `data/data_unread_value.cpp`, `menu/menu_mark_as_read.cpp`, `api/api_unread_things.cpp` |
| Chat suggestions bar | Top-of-list prompts (set up 2FA, etc.) | `dialogs/dialogs_top_bar_suggestion.cpp`, `data/components/promo_suggestions.cpp` |
| Peer menu actions | Pin, archive, clear history, leave, block, add to folder | `window/window_peer_menu.cpp` |
| Multi-window | Detach chats to separate windows, saved windows | `window/window_separate_id.cpp`, `window/window_saved_windows.cpp` |
| Chat preview | Peek at chat from list or link | `window/window_chat_preview.cpp`, `history/view/history_view_chat_preview.cpp` |
| Chat switcher | Ctrl-Tab style quick switcher | `window/window_chat_switch_process.cpp` |

## 5. Account and privacy

| Feature | Description | Paths |
|---|---|---|
| Login | Phone, code, 2FA password, QR login, email login setup | `intro/`, `settings/settings_intro.cpp`, `window/window_setup_email.cpp` |
| Multi-account | Several accounts in one app, account switcher, per-account tray menu | `main/main_domain.cpp`, `main/main_account.cpp`, `main/main_session.cpp`, `tray_accounts_menu.cpp`, `window/window_main_menu.cpp` |
| Privacy settings | Phone, last seen, profile photo, forwards, calls, invites, voice messages, birthday, gifts rules with allow/deny lists | `api/api_user_privacy.cpp`, `boxes/edit_privacy_box.cpp`, `settings/settings_privacy_controllers.cpp`, `settings/sections/settings_privacy_security.cpp` |
| Global privacy options | Archive new chats from unknown, hide read time, paid messages | `api/api_global_privacy.cpp` |
| Active sessions | List and terminate other devices, confirm unconfirmed logins | `settings/sections/settings_active_sessions.cpp`, `api/api_authorizations.cpp`, `boxes/unconfirmed_auth_denied_box.cpp` |
| Connected websites | Websites authorized via Telegram login | `settings/sections/settings_websites.cpp`, `api/api_websites.cpp` |
| Two-step verification | Cloud password with hint, recovery email, login email | `settings/cloud_password/`, `api/api_cloud_password.cpp`, `core/core_cloud_password.cpp` |
| Passkeys | WebAuthn passkey login | `settings/sections/settings_passkeys.cpp`, `webauthn/`, `data/components/passkeys.cpp` |
| Local passcode and auto-lock | App-level passcode, auto-lock timeout, unlock screen, lock shortcut | `settings/sections/settings_local_passcode.cpp`, `boxes/passcode_box.cpp`, `boxes/auto_lock_box.cpp`, `window/window_lock_widgets.cpp`, `window/window_unlock_passcode_box.cpp` |
| Account self-destruct | Delete account after inactivity | `boxes/self_destruction_box.cpp`, `api/api_self_destruct.cpp` |
| Sensitive content toggle | Show/hide sensitive media | `api/api_sensitive_content.cpp` |
| Screenshot protection | Prevent screenshots of protected windows | `core/core_screenshot_protection.cpp` |
| Telegram Passport | Store/share identity documents encrypted with bots/services | `passport/` |
| Profile editing | Name, bio, username, photo, birthday, emoji status, profile color | `settings/sections/settings_information.cpp`, `boxes/username_box.cpp`, `api/api_peer_photo.cpp`, `api/api_user_names.cpp`, `api/api_peer_colors.cpp` |
| Block list | Blocked users management | `settings/sections/settings_blocked_peers.cpp` |
| Local storage encryption | Local cache and key-file storage per account | `storage/storage_account.cpp`, `storage/storage_domain.cpp`, `storage/localstorage.cpp` |

## 6. Calls and video chats

| Feature | Description | Paths |
|---|---|---|
| 1:1 voice/video calls | WebRTC-based calls, emoji fingerprint for verification, rating | `calls/calls_call.cpp`, `calls/calls_panel.cpp`, `calls/calls_controller_webrtc.cpp`, `calls/calls_emoji_fingerprint.cpp`, `calls/calls_rate_call.cpp` |
| Call settings | Device selection, noise suppression, ringtone | `settings/sections/settings_calls.cpp`, `calls/calls_instance.cpp` |
| Call history | Recent calls list | `calls/calls_box_controller.cpp` |
| Group calls / voice chats | Multi-party audio/video, screen share, speaker viewport, invite, join-as | `calls/group/` (`calls_group_call.cpp`, `calls_group_panel.cpp`, `calls_group_viewport*.cpp`, `calls_choose_join_as.cpp`) |
| RTMP streams | Live streaming into channels | `calls/group/calls_group_rtmp.cpp` |
| Group call chat/encryption/stars | In-call messages, E2E for group calls, star gifts in calls | `calls/group/calls_group_messages.cpp`, `calls/group/calls_group_message_encryption.cpp`, `calls/group/calls_group_stars_box.cpp`, `tde2e/` |
| Call bars and scheduling | Top bar for active call, scheduled voice chats | `calls/calls_top_bar.cpp`, `ui/chat/group_call_bar.cpp`, `history/view/history_view_group_call_bar.cpp`, `data/data_group_call.cpp` |

## 7. Premium, Stars, gifts, payments

| Feature | Description | Paths |
|---|---|---|
| Premium subscription | Premium upsell, previews, limits, gift premium | `settings/sections/settings_premium.cpp`, `boxes/premium_preview_box.cpp`, `boxes/premium_limits_box.cpp`, `boxes/gift_premium_box.cpp`, `api/api_premium.cpp`, `data/data_premium_limits.cpp` |
| Stars (credits) | Star balance, top-up, history, subscriptions, send stars | `settings/sections/settings_credits.cpp`, `settings/settings_credits_graphics.cpp`, `api/api_credits.cpp`, `data/components/credits.cpp`, `boxes/send_credits_box.cpp`, `boxes/gift_credits_box.cpp` |
| Star gifts | Send/receive collectible gifts, upgrade, resale, auctions, craft, transfer | `boxes/star_gift_box.cpp`, `boxes/star_gift_resale_box.cpp`, `boxes/star_gift_auction_box.cpp`, `boxes/star_gift_craft_box.cpp`, `boxes/transfer_gift_box.cpp`, `data/data_star_gift.cpp`, `info/peer_gifts/`, `history/view/media/history_view_unique_gift.cpp` |
| Giveaways and boosts | Channel boosts, giveaways | `history/view/media/history_view_giveaway.cpp`, `boxes/peers/replace_boost_box.cpp`, `data/data_boosts.h` |
| Payments / checkout | Bot invoices, shipping, saved cards, Stripe/SmartGlocal | `payments/` (`payments_checkout_process.cpp`, `payments_form.cpp`, `stripe/`, `smartglocal/`), `history/view/media/history_view_invoice.cpp` |
| Earnings / TON | Channel earnings, TON wallet pieces | `data/data_channel_earn.h`, `data/data_credits_earn.h`, `api/api_earn.cpp`, `core/ton_explorer_url.cpp`, `wallet/` |
| Channel/bot statistics | Charts for growth, views, boosts | `statistics/`, `info/statistics/`, `api/api_statistics.cpp` |
| Business features (premium) | Business hours, location, chatbots, quick replies | `settings/business/`, `settings/sections/settings_business.cpp` |

## 8. Settings and app shell

| Feature | Description | Paths |
|---|---|---|
| Main settings menu | Hierarchical settings with in-settings search and key navigation | `settings/sections/settings_main.cpp`, `settings/settings_search.cpp`, `settings/settings_builder.cpp`, `settings/settings_key_navigation.cpp` |
| Chat appearance | Chat text size, bubble style, day/night mode, emoji/sticker options | `settings/sections/settings_chat.cpp`, `ui/chat/chat_style.cpp` |
| Themes | Built-in, cloud and custom themes, theme editor/preview, accent color | `window/themes/`, `data/data_cloud_themes.cpp`, `ui/chat/chat_theme.cpp`, `ui/chat/choose_theme_controller.cpp` |
| Chat backgrounds | Wallpapers (images, gradients, patterns), per-chat wallpaper, preview | `boxes/background_box.cpp`, `boxes/background_preview_box.cpp`, `data/data_wall_paper.cpp` |
| Language | UI language packs, cloud langpacks, translations | `boxes/language_box.cpp`, `lang/`, `lang/lang_cloud_manager` |
| Keyboard shortcuts | Customizable shortcuts | `settings/sections/settings_shortcuts.cpp`, `core/shortcuts.cpp` |
| Proxy / connection | MTProto proxy, SOCKS5, HTTP; proxy rotation; TCP/HTTP transport choice | `boxes/connection_box.cpp`, `core/core_settings_proxy.cpp`, `core/proxy_rotation_manager.cpp`, `mtproto/` |
| Data and storage | Cache limits, clear cache, download path, auto-download | `settings/sections/settings_local_storage.cpp`, `boxes/download_path_box.cpp`, `storage/` |
| Advanced | Updates, hardware acceleration, scale, tray behavior, spellcheck, experimental flags | `settings/sections/settings_advanced.cpp`, `settings/settings_experimental.cpp`, `settings/settings_scale_preview.cpp`, `core/update_checker.cpp` |
| Power saving | Reduce animations and effects | `settings/settings_power_saving.cpp` |
| Notifications settings | Per-type sound/preview/counters, reactions notifications, DND | `settings/sections/settings_notifications.cpp`, `settings/sections/settings_notifications_type.cpp`, `data/notify/` |
| Deep links | `tg://` and `t.me` URL handling, external control | `core/local_url_handlers.cpp`, `core/deep_links/`, `core/external_control.cpp` |
| App updater | Built-in update download, signature verify, changelogs | `core/update_checker.cpp`, `core/update_verify.cpp`, `core/changelogs.cpp` |
| Adaptive layout | Narrow/wide window modes, section stack | `window/window_adaptive.cpp`, `window/window_session_controller.cpp`, `window/window_slide_animation.cpp` |
| Connection status | Connecting/updating banner | `window/window_connecting_widget.cpp` |
| Platform integration | Per-OS tray, dock, title bar, file bookmarks, geo, text recognition, translate provider | `platform/linux`, `platform/mac`, `platform/win`, `platform/platform_integration.cpp` |
| Crash reporting and logs | Crash dialog, logs | `core/crash_reports.cpp`, `logs.cpp` |

## 9. Export

| Feature | Description | Paths |
|---|---|---|
| Data export | Export account data (chats, media, contacts, sessions, stories) to HTML or JSON with date range, size limits, per-chat selection | `export/export_controller.cpp`, `export/export_settings.cpp`, `export/export_api_wrap.cpp`, `export/data/`, `export/output/`, `export/view/` |
| Export manager | Export progress UI and lifecycle | `export/export_manager.cpp` |

## 10. Tray and notifications

| Feature | Description | Paths |
|---|---|---|
| Tray icon | Show/hide window, unread badge, accounts submenu, quit | `tray.cpp`, `tray_accounts_menu.cpp`, `platform/platform_tray.h`, `window/main_window.cpp` |
| Desktop notifications | Native notifications with sender, preview, reply and mark-read actions; custom in-app popup fallback | `window/notifications_manager.cpp`, `window/notifications_manager_default.cpp`, `platform/platform_notifications_manager.h` |
| Notification sounds and DND | Message sounds, mute-all, silent mode, call ringtones | `window/notifications_utilities.cpp`, `boxes/ringtones_box.cpp` |
| Badge counters | Dock/taskbar/tray unread count, with muted-included options | `window/main_window.cpp`, `data/data_unread_value.cpp` |

---

## 11. Classification for the new multi-messenger client

Assumptions: Telegram via TDLib, Bale as a second transport, messages carried as encrypted envelopes. MVP = needed for first usable release; Later = valuable but deferrable; Skip = Telegram-only or not worth cloning. "Transport-limited" = depends on what Bale exposes.

| Feature | Class | Notes |
|---|---|---|
| Chat list with unread, pin, preview | MVP | Core; unify across transports |
| Send/receive text | MVP | Envelope payload |
| Reply | MVP | Envelope carries reply reference |
| Edit | MVP | Needs transport edit support |
| Delete (me / everyone) | MVP | Transport-limited |
| Reactions | MVP | Map to transport reactions or envelope-level |
| Files and photos (upload/download, captions) | MVP | Includes progress and resume |
| Albums (grouped media) | Later | |
| Search (in chat + global, local index) | MVP | Prefer local index over envelope-decrypted content |
| Folders-lite (user-defined chat groups, archive) | MVP | Local-only folders acceptable; Telegram cloud folders optional |
| Multi-account | MVP | Per transport and per account |
| Mute, unread counters, desktop notifications, tray | MVP | |
| Basic formatting (bold/italic/code/links) | MVP | Define in envelope schema |
| Forward | MVP | Basic forward; options Later |
| Typing / read state | Later | Transport-limited |
| Pin messages | Later | |
| Drafts (local) | MVP | Cloud-synced drafts Later |
| Saved Messages | Later | Simple self-chat first |
| Voice messages | Later | Record, waveform, playback |
| Round video messages | Later | |
| Video/audio playback and music player | Later | Basic inline playback in MVP |
| Stickers and GIFs | Later | Sticker sets Telegram-specific; GIF search Later |
| Emoji picker (unicode) | MVP | |
| Custom emoji / animated emoji | Later | |
| Mentions, hashtags, autocomplete | Later | |
| Link previews | Later | |
| Instant View | Skip | |
| Polls | Later | |
| Todo lists | Later | |
| Scheduled / silent send | Later | |
| Translate, voice transcription | Later | |
| AI compose | Skip | |
| Quotes (partial reply) | Later | |
| Message TTL / view-once | Later | Natural fit for envelopes |
| Groups and supergroups (basic chat, members) | Later | Admin tooling and permissions Later |
| Channels (read, comments) | Later | |
| Forum topics | Later | |
| Communities | Skip | |
| Invite links, join requests, admin log | Later | |
| Inline bots | Later | |
| Bots (commands, keyboards) | Later | |
| Web apps / mini apps | Telegram-only / skip | |
| Secret chats | Telegram-only / skip | Envelope encryption supersedes; Desktop lacks them anyway |
| Stories | Skip | |
| Calls and video chats (1:1, group, RTMP) | Telegram-only / skip | |
| Premium, business tools | Telegram-only / skip | |
| Stars, gifts, paid reactions, giveaways, boosts | Telegram-only / skip | |
| Payments, checkout | Telegram-only / skip | |
| Passport | Telegram-only / skip | |
| Statistics, earnings, TON | Telegram-only / skip | |
| Sponsored messages, support tooling | Telegram-only / skip | |
| Privacy settings (Telegram cloud) | Later | Only those exposed by each transport |
| Sessions list / connected websites | Later | Telegram-only data; sessions Later |
| 2FA cloud password | Later | Handled by TDLib auth flow; settings UI Later |
| Passkeys | Skip | |
| Local passcode and auto-lock | Later | Strongly recommended soon given encrypted-envelope story |
| Appearance (light/dark, text size) | MVP | Themes/wallpapers Later |
| Custom themes, cloud themes, wallpapers | Later | |
| Language / localization | Later | |
| Shortcuts | Later | |
| Proxy settings | Later | Important for Bale/Telegram reachability; consider promoting |
| Data and storage (cache limits, downloads) | Later | |
| Auto-download rules | Later | |
| Export (own data) | Later | |
| Deep links (`tg://`, `t.me`) | Later | |
| Multi-window, chat preview, switcher | Later | |
| Auto-updater | Later | |
