using System;

namespace STS2Mobile.Launcher.Sections;

// Confirmation request raised by the Mod Hub panes and forwarded to
// LauncherView.ShowConfirmation. okLabel / cancelLabel null keep the default
// 확인 / 취소 pair; pass a verb that answers the question where one reads
// better ("구독 취소" / "닫기" for an unsubscribe prompt, see docs/ko-glossary.md).
public delegate void ConfirmHandler(
    string message,
    Action onConfirmed,
    Action onCancelled,
    string okLabel = null,
    string cancelLabel = null
);
