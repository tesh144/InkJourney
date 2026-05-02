#import <UIKit/UIKit.h>
#import <AVFoundation/AVFoundation.h>
#include "UnityInterface.h"

static NSString *const kBridgeObject  = @"NativeTextEditorBridge";
static const CGFloat kNavBarHeight    = 44.0;
static const CGFloat kTitleHeight     = 52.0;
static const CGFloat kDividerHeight   = 0.5;
static const CGFloat kBodyFontSize    = 17.0;
static const CGFloat kTitleFontSize   = 24.0;

// ─────────────────────────────────────────────────────────────────────────────
@interface NativeTextEditorController : NSObject <UITextViewDelegate, UITextFieldDelegate>
+ (instancetype)shared;
- (void)showWithTitle:(NSString *)title content:(NSString *)content;
- (void)hide;
@end

@implementation NativeTextEditorController {
    UIView                     *_container;
    UITextField                *_titleField;
    UITextView                 *_textView;
    UIFont                     *_bodyFont;
    NSDictionary               *_defaultAttrs;
    AVCaptureSession           *_captureSession;
    AVCaptureVideoPreviewLayer *_previewLayer;
    BOOL                        _hasCameraBackground;
}

+ (instancetype)shared {
    static NativeTextEditorController *s;
    static dispatch_once_t t;
    dispatch_once(&t, ^{ s = [self new]; });
    return s;
}

// ── Show ─────────────────────────────────────────────────────────────────────

- (void)showWithTitle:(NSString *)title content:(NSString *)content {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (self->_container) return;

        UIWindow *window = [UIApplication sharedApplication].keyWindow
                        ?: [UIApplication sharedApplication].windows.firstObject;
        CGRect screen    = window.bounds;
        UIEdgeInsets safe = window.safeAreaInsets;

        self->_bodyFont = [UIFont systemFontOfSize:kBodyFontSize];

        // Paragraph style — comfortable line + paragraph spacing
        NSMutableParagraphStyle *para = [NSMutableParagraphStyle new];
        para.lineSpacing       = 5;
        para.paragraphSpacing  = 12;
        // Note: _hasCameraBackground is set further down — captured via block after setup.
        // We patch typingAttributes after the camera check via primaryText set below.
        self->_defaultAttrs = @{
            NSFontAttributeName:            self->_bodyFont,
            NSForegroundColorAttributeName: [UIColor labelColor], // patched below
            NSParagraphStyleAttributeName:  para
        };

        // ── Container ──────────────────────────────────────────────────────
        self->_container = [[UIView alloc] initWithFrame:screen];
        self->_container.alpha = 0;

        // ── Camera background ──────────────────────────────────────────────
        AVAuthorizationStatus camStatus =
            [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo];
        self->_hasCameraBackground = (camStatus == AVAuthorizationStatusAuthorized);

        if (self->_hasCameraBackground) {
            self->_container.backgroundColor = [UIColor blackColor];
            [self setupCameraBackground:screen];

            // Semi-transparent dark veil so text remains readable
            UIView *veil = [[UIView alloc] initWithFrame:screen];
            veil.backgroundColor = [UIColor colorWithWhite:0 alpha:0.60];
            [self->_container addSubview:veil];
        } else {
            self->_container.backgroundColor = [UIColor systemBackgroundColor];
        }

        // ── Colour palette — adapts to camera vs solid background ──────────
        UIColor *primaryText   = self->_hasCameraBackground
            ? [UIColor whiteColor]
            : [UIColor labelColor];

        // Patch default text attributes to match background
        if (self->_hasCameraBackground) {
            NSMutableDictionary *patched = [self->_defaultAttrs mutableCopy];
            patched[NSForegroundColorAttributeName] = primaryText;
            self->_defaultAttrs = [patched copy];
        }
        UIColor *secondaryText = self->_hasCameraBackground
            ? [UIColor colorWithWhite:1 alpha:0.65]
            : [UIColor secondaryLabelColor];
        UIColor *separatorColor = self->_hasCameraBackground
            ? [UIColor colorWithWhite:1 alpha:0.25]
            : [UIColor colorWithWhite:0.82 alpha:1];
        UIColor *navBgColor    = self->_hasCameraBackground
            ? [UIColor colorWithWhite:0 alpha:0.30]
            : [UIColor systemBackgroundColor];

        // ── Navigation bar ─────────────────────────────────────────────────
        CGFloat navH = safe.top + kNavBarHeight;
        UIView *navBar = [[UIView alloc] initWithFrame:CGRectMake(0, 0, screen.size.width, navH)];
        navBar.backgroundColor = navBgColor;

        // nav bottom border
        UIView *navBorder = [[UIView alloc] initWithFrame:CGRectMake(0, navH - 0.5, screen.size.width, 0.5)];
        navBorder.backgroundColor = separatorColor;
        [navBar addSubview:navBorder];

        // Cancel
        UIButton *cancelBtn = [UIButton buttonWithType:UIButtonTypeSystem];
        cancelBtn.frame = CGRectMake(12, safe.top, 72, kNavBarHeight);
        [cancelBtn setTitle:@"Cancel" forState:UIControlStateNormal];
        [cancelBtn setTitleColor:secondaryText forState:UIControlStateNormal];
        cancelBtn.titleLabel.font = [UIFont systemFontOfSize:16];
        [cancelBtn addTarget:self action:@selector(onCancel) forControlEvents:UIControlEventTouchUpInside];
        [navBar addSubview:cancelBtn];

        // Done
        UIButton *doneBtn = [UIButton buttonWithType:UIButtonTypeSystem];
        doneBtn.frame = CGRectMake(screen.size.width - 84, safe.top, 72, kNavBarHeight);
        [doneBtn setTitle:@"Done" forState:UIControlStateNormal];
        [doneBtn setTitleColor:primaryText forState:UIControlStateNormal];
        doneBtn.titleLabel.font = [UIFont boldSystemFontOfSize:16];
        [doneBtn addTarget:self action:@selector(onDone) forControlEvents:UIControlEventTouchUpInside];
        [navBar addSubview:doneBtn];

        [self->_container addSubview:navBar];

        // ── Title field ────────────────────────────────────────────────────
        CGFloat titleY = navH + 10;
        self->_titleField = [[UITextField alloc] initWithFrame:CGRectMake(20, titleY, screen.size.width - 40, kTitleHeight)];
        self->_titleField.text              = title;
        self->_titleField.font              = [UIFont boldSystemFontOfSize:kTitleFontSize];
        self->_titleField.textColor         = primaryText;
        self->_titleField.backgroundColor   = [UIColor clearColor];
        self->_titleField.borderStyle       = UITextBorderStyleNone;
        self->_titleField.returnKeyType     = UIReturnKeyNext;
        self->_titleField.keyboardAppearance = self->_hasCameraBackground
            ? UIKeyboardAppearanceDark : UIKeyboardAppearanceDefault;
        self->_titleField.delegate          = self;
        self->_titleField.attributedPlaceholder = [[NSAttributedString alloc] initWithString:@"Title"
            attributes:@{NSForegroundColorAttributeName: secondaryText,
                         NSFontAttributeName: [UIFont systemFontOfSize:kTitleFontSize]}];
        [self->_container addSubview:self->_titleField];

        // ── Divider ────────────────────────────────────────────────────────
        CGFloat dividerY = titleY + kTitleHeight + 6;
        UIView *divider = [[UIView alloc] initWithFrame:CGRectMake(20, dividerY, screen.size.width - 40, kDividerHeight)];
        divider.backgroundColor = separatorColor;
        [self->_container addSubview:divider];

        // ── Body text view ─────────────────────────────────────────────────
        CGFloat bodyY = dividerY + kDividerHeight + 12;
        self->_textView = [[UITextView alloc] initWithFrame:
            CGRectMake(20, bodyY, screen.size.width - 40, screen.size.height - bodyY - safe.bottom)];
        self->_textView.backgroundColor         = [UIColor clearColor];
        self->_textView.textColor               = primaryText;
        self->_textView.font                    = self->_bodyFont;
        self->_textView.keyboardAppearance      = self->_hasCameraBackground
            ? UIKeyboardAppearanceDark : UIKeyboardAppearanceDefault;
        self->_textView.autocorrectionType      = UITextAutocorrectionTypeDefault;
        self->_textView.spellCheckingType       = UITextSpellCheckingTypeYes;
        self->_textView.autocapitalizationType  = UITextAutocapitalizationTypeSentences;
        self->_textView.delegate                = self;
        self->_textView.inputAccessoryView      = [self makeFormattingBar];

        NSString *cleanContent = [self stripTMPTags:content];
        self->_textView.attributedText = [[NSAttributedString alloc]
            initWithString:cleanContent attributes:self->_defaultAttrs];
        self->_textView.typingAttributes = self->_defaultAttrs;

        [self->_container addSubview:self->_textView];

        // ── Keyboard notification ──────────────────────────────────────────
        [[NSNotificationCenter defaultCenter]
            addObserver:self selector:@selector(keyboardWillChange:)
            name:UIKeyboardWillChangeFrameNotification object:nil];

        [window addSubview:self->_container];
        [UIView animateWithDuration:0.2 animations:^{ self->_container.alpha = 1; }];
        [self->_textView becomeFirstResponder];
        self->_textView.selectedRange = NSMakeRange(self->_textView.text.length, 0);
    });
}

// ── Camera pre-warm ───────────────────────────────────────────────────────────
// Call when the create panel opens. Builds + starts the AVCaptureSession on a
// background thread so the first editor open has no main-thread stall.

- (void)prewarm {
    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        if (self->_captureSession) return;

        AVAuthorizationStatus status =
            [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo];
        if (status != AVAuthorizationStatusAuthorized) return;

        AVCaptureDevice *cam = [AVCaptureDevice defaultDeviceWithMediaType:AVMediaTypeVideo];
        if (!cam) return;

        NSError *err = nil;
        AVCaptureDeviceInput *input = [AVCaptureDeviceInput deviceInputWithDevice:cam error:&err];
        if (!input || err) return;

        AVCaptureSession *session = [AVCaptureSession new];
        session.sessionPreset = AVCaptureSessionPreset640x480;
        [session addInput:input];
        [session startRunning];

        dispatch_async(dispatch_get_main_queue(), ^{
            if (!self->_captureSession)
                self->_captureSession = session;
            else
                [session stopRunning]; // editor opened before prewarm finished — discard
        });
    });
}

// Call when the create panel closes without the editor having been opened,
// so the idle session doesn't drain the battery.
- (void)stopPrewarm {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (self->_container) return; // editor is currently showing — leave it alone
        if (!self->_captureSession) return;

        AVCaptureSession *s = self->_captureSession;
        self->_captureSession = nil;
        dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
            [s stopRunning];
        });
    });
}

// ── Camera background ─────────────────────────────────────────────────────────

- (void)setupCameraBackground:(CGRect)frame {
    if (!_captureSession) {
        // Not pre-warmed (e.g. prewarm was skipped or lost the race) — build now.
        AVCaptureDevice *cam = [AVCaptureDevice defaultDeviceWithMediaType:AVMediaTypeVideo];
        if (!cam) { _hasCameraBackground = NO; return; }

        NSError *err = nil;
        AVCaptureDeviceInput *input = [AVCaptureDeviceInput deviceInputWithDevice:cam error:&err];
        if (!input || err) { _hasCameraBackground = NO; return; }

        _captureSession = [AVCaptureSession new];
        _captureSession.sessionPreset = AVCaptureSessionPreset640x480;
        [_captureSession addInput:input];

        dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
            [self->_captureSession startRunning];
        });
    }

    // Session is either pre-warmed and running, or just created above.
    _previewLayer = [AVCaptureVideoPreviewLayer layerWithSession:_captureSession];
    _previewLayer.frame        = frame;
    _previewLayer.videoGravity = AVLayerVideoGravityResizeAspectFill;
    if (_previewLayer.connection.isVideoOrientationSupported)
        _previewLayer.connection.videoOrientation = AVCaptureVideoOrientationPortrait;

    [_container.layer insertSublayer:_previewLayer atIndex:0];
}

// ── Keyboard ─────────────────────────────────────────────────────────────────

- (void)keyboardWillChange:(NSNotification *)note {
    UIWindow *window  = [UIApplication sharedApplication].keyWindow
                     ?: [UIApplication sharedApplication].windows.firstObject;
    UIEdgeInsets safe = window.safeAreaInsets;
    CGRect kbFrame    = [note.userInfo[UIKeyboardFrameEndUserInfoKey] CGRectValue];
    CGFloat kbTop     = kbFrame.origin.y;

    CGFloat navH    = safe.top + kNavBarHeight;
    CGFloat bodyY   = navH + 10 + kTitleHeight + 6 + kDividerHeight + 12;
    CGFloat newH    = MAX(kbTop - bodyY, 80);

    [UIView animateWithDuration:0.25 animations:^{
        self->_textView.frame = CGRectMake(
            self->_textView.frame.origin.x,
            self->_textView.frame.origin.y,
            self->_textView.frame.size.width,
            newH
        );
    }];
}

// ── Formatting toolbar ────────────────────────────────────────────────────────

- (UIView *)makeFormattingBar {
    UIToolbar *bar = [[UIToolbar alloc] initWithFrame:CGRectMake(0, 0, 320, 44)];
    bar.barStyle         = UIBarStyleDefault;
    bar.translucent      = YES;
    bar.autoresizingMask = UIViewAutoresizingFlexibleWidth;

    UIBarButtonItem *bold      = [self symbolItem:@"bold"        action:@selector(toggleBold)];
    UIBarButtonItem *italic    = [self symbolItem:@"italic"      action:@selector(toggleItalic)];
    UIBarButtonItem *underline = [self symbolItem:@"underline"   action:@selector(toggleUnderline)];
    UIBarButtonItem *bullet    = [self symbolItem:@"list.bullet" action:@selector(toggleBullet)];

    UIBarButtonItem *flex = [[UIBarButtonItem alloc]
        initWithBarButtonSystemItem:UIBarButtonSystemItemFlexibleSpace target:nil action:nil];

    bar.items = @[bold, italic, underline, bullet, flex];
    return bar;
}

- (UIBarButtonItem *)symbolItem:(NSString *)name action:(SEL)sel {
    UIImage *img = [UIImage systemImageNamed:name];
    UIBarButtonItem *item = [[UIBarButtonItem alloc]
        initWithImage:img style:UIBarButtonItemStylePlain target:self action:sel];
    item.tintColor = _hasCameraBackground
        ? [UIColor colorWithWhite:1 alpha:0.75]
        : [UIColor secondaryLabelColor];
    return item;
}

// ── Formatting actions ────────────────────────────────────────────────────────

- (void)toggleBold   { [self toggleTrait:UIFontDescriptorTraitBold];   }
- (void)toggleItalic { [self toggleTrait:UIFontDescriptorTraitItalic]; }

- (void)toggleTrait:(UIFontDescriptorSymbolicTraits)trait {
    NSRange range = _textView.selectedRange;

    if (range.length == 0) {
        UIFont *cur = _textView.typingAttributes[NSFontAttributeName] ?: _bodyFont;
        NSMutableDictionary *a = [_textView.typingAttributes mutableCopy];
        a[NSFontAttributeName] = [self font:cur toggleTrait:trait];
        _textView.typingAttributes = a;
        return;
    }

    NSMutableAttributedString *as = [_textView.attributedText mutableCopy];
    __block BOOL allHave = YES;
    [as enumerateAttribute:NSFontAttributeName inRange:range options:0 usingBlock:^(UIFont *f, NSRange r, BOOL *s) {
        if (!((f ?: self->_bodyFont).fontDescriptor.symbolicTraits & trait)) { allHave = NO; *s = YES; }
    }];
    [as enumerateAttribute:NSFontAttributeName inRange:range options:0 usingBlock:^(UIFont *f, NSRange r, BOOL *_) {
        UIFont *cur   = f ?: self->_bodyFont;
        UIFontDescriptorSymbolicTraits t = cur.fontDescriptor.symbolicTraits;
        t = allHave ? (t & ~trait) : (t | trait);
        UIFontDescriptor *desc = [cur.fontDescriptor fontDescriptorWithSymbolicTraits:t];
        UIFont *next = [UIFont fontWithDescriptor:desc size:cur.pointSize] ?: cur;
        [as addAttribute:NSFontAttributeName value:next range:r];
    }];
    _textView.attributedText = as;
    _textView.selectedRange  = range;
}

- (void)toggleBullet {
    NSMutableAttributedString *as  = [_textView.attributedText mutableCopy];
    NSString *str  = as.string;
    NSRange sel    = _textView.selectedRange;
    NSUInteger ls  = 0, le = 0, ce = 0;
    [str getLineStart:&ls end:&le contentsEnd:&ce forRange:sel];

    NSString *bullet = @"• ";
    NSString *line   = [str substringWithRange:NSMakeRange(ls, ce - ls)];

    if ([line hasPrefix:bullet]) {
        [as deleteCharactersInRange:NSMakeRange(ls, bullet.length)];
        _textView.attributedText = as;
        NSUInteger newLoc = sel.location >= ls + bullet.length ? sel.location - bullet.length : ls;
        _textView.selectedRange = NSMakeRange(newLoc, 0);
    } else {
        NSAttributedString *ins = [[NSAttributedString alloc]
            initWithString:bullet attributes:_textView.typingAttributes];
        [as insertAttributedString:ins atIndex:ls];
        _textView.attributedText = as;
        _textView.selectedRange  = NSMakeRange(sel.location + bullet.length, sel.length);
    }
}

- (void)toggleUnderline {
    NSRange range = _textView.selectedRange;

    if (range.length == 0) {
        NSMutableDictionary *a = [_textView.typingAttributes mutableCopy];
        NSNumber *cur = a[NSUnderlineStyleAttributeName];
        a[NSUnderlineStyleAttributeName] = (cur && cur.integerValue != NSUnderlineStyleNone)
            ? @(NSUnderlineStyleNone) : @(NSUnderlineStyleSingle);
        _textView.typingAttributes = a;
        return;
    }

    NSMutableAttributedString *as = [_textView.attributedText mutableCopy];
    __block BOOL allHave = YES;
    [as enumerateAttribute:NSUnderlineStyleAttributeName inRange:range options:0
        usingBlock:^(NSNumber *v, NSRange r, BOOL *s) {
        if (!v || v.integerValue == NSUnderlineStyleNone) { allHave = NO; *s = YES; }
    }];
    NSNumber *style = allHave ? @(NSUnderlineStyleNone) : @(NSUnderlineStyleSingle);
    [as addAttribute:NSUnderlineStyleAttributeName value:style range:range];
    _textView.attributedText = as;
    _textView.selectedRange  = range;
}

// ── Font helpers ──────────────────────────────────────────────────────────────

- (UIFont *)font:(UIFont *)f toggleTrait:(UIFontDescriptorSymbolicTraits)trait {
    UIFontDescriptorSymbolicTraits t = f.fontDescriptor.symbolicTraits;
    t = (t & trait) ? (t & ~trait) : (t | trait);
    UIFontDescriptor *d = [f.fontDescriptor fontDescriptorWithSymbolicTraits:t];
    return [UIFont fontWithDescriptor:d size:f.pointSize] ?: f;
}

- (UIFont *)makeFont:(UIFont *)base bold:(BOOL)b italic:(BOOL)i {
    UIFontDescriptorSymbolicTraits t = base.fontDescriptor.symbolicTraits;
    if (b) t |= UIFontDescriptorTraitBold;
    if (i) t |= UIFontDescriptorTraitItalic;
    UIFontDescriptor *d = [base.fontDescriptor fontDescriptorWithSymbolicTraits:t];
    return [UIFont fontWithDescriptor:d size:base.pointSize] ?: base;
}

// ── Done / Cancel ─────────────────────────────────────────────────────────────

- (void)onDone {
    NSString *title   = [_titleField.text copy] ?: @"";
    NSString *content = [self toTMPMarkup:_textView.attributedText];
    NSString *combined = [NSString stringWithFormat:@"%@\x1E%@", title, content];
    [self hide];
    UnitySendMessage([kBridgeObject UTF8String], "OnTextEditComplete", [combined UTF8String]);
}

- (void)onCancel {
    [self hide];
    UnitySendMessage([kBridgeObject UTF8String], "OnTextEditCancelled", "");
}

- (void)hide {
    dispatch_async(dispatch_get_main_queue(), ^{
        [[NSNotificationCenter defaultCenter]
            removeObserver:self name:UIKeyboardWillChangeFrameNotification object:nil];
        [self->_textView resignFirstResponder];

        // Stop capture session before the view disappears
        if (self->_captureSession) {
            AVCaptureSession *s = self->_captureSession;
            self->_captureSession = nil;
            self->_previewLayer   = nil;
            dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
                [s stopRunning];
            });
        }

        [UIView animateWithDuration:0.2 animations:^{ self->_container.alpha = 0; }
                         completion:^(BOOL _) {
            [self->_container removeFromSuperview];
            self->_container          = nil;
            self->_textView           = nil;
            self->_titleField         = nil;
            self->_hasCameraBackground = NO;
        }];
    });
}

// ── Markup helpers ────────────────────────────────────────────────────────────

- (NSString *)toTMPMarkup:(NSAttributedString *)attr {
    if (!attr || attr.length == 0) return @"";
    NSMutableString *out = [NSMutableString new];
    [attr enumerateAttributesInRange:NSMakeRange(0, attr.length) options:0
                          usingBlock:^(NSDictionary *a, NSRange r, BOOL *_) {
        UIFont *f = a[NSFontAttributeName] ?: self->_bodyFont;
        UIFontDescriptorSymbolicTraits t = f.fontDescriptor.symbolicTraits;
        BOOL bold      = (t & UIFontDescriptorTraitBold)   != 0;
        BOOL italic    = (t & UIFontDescriptorTraitItalic) != 0;
        BOOL underline = [a[NSUnderlineStyleAttributeName] integerValue] == NSUnderlineStyleSingle;
        NSString *s = [attr.string substringWithRange:r];
        if (bold)      s = [NSString stringWithFormat:@"<b>%@</b>",   s];
        if (italic)    s = [NSString stringWithFormat:@"<i>%@</i>",   s];
        if (underline) s = [NSString stringWithFormat:@"<u>%@</u>",   s];
        [out appendString:s];
    }];
    return out;
}

- (NSString *)stripTMPTags:(NSString *)s {
    if (!s || s.length == 0) return @"";
    NSRegularExpression *re = [NSRegularExpression
        regularExpressionWithPattern:@"<[^>]+>" options:0 error:nil];
    return [re stringByReplacingMatchesInString:s options:0
        range:NSMakeRange(0, s.length) withTemplate:@""];
}

// ── UITextFieldDelegate ───────────────────────────────────────────────────────

- (BOOL)textFieldShouldReturn:(UITextField *)tf {
    [_textView becomeFirstResponder];
    return NO;
}

@end

// ─────────────────────────────────────────────────────────────────────────────
// C entry points
// ─────────────────────────────────────────────────────────────────────────────
extern "C" {

void NativeTextEditor_Show(const char *title, const char *content, const char *placeholder) {
    NSString *t = title   ? [NSString stringWithUTF8String:title]   : @"";
    NSString *c = content ? [NSString stringWithUTF8String:content] : @"";
    [[NativeTextEditorController shared] showWithTitle:t content:c];
}

void NativeTextEditor_Hide() {
    [[NativeTextEditorController shared] hide];
}

void NativeTextEditor_Prewarm() {
    [[NativeTextEditorController shared] prewarm];
}

void NativeTextEditor_StopPrewarm() {
    [[NativeTextEditorController shared] stopPrewarm];
}

} // extern "C"
