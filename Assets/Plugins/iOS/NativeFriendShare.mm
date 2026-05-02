#import <UIKit/UIKit.h>

extern "C" {
    void _ShareText(const char* text) {
        NSString* shareText = [NSString stringWithUTF8String:text];
        UIActivityViewController* vc = [[UIActivityViewController alloc]
            initWithActivityItems:@[shareText] applicationActivities:nil];

        UIViewController* root = [UIApplication sharedApplication].keyWindow.rootViewController;
        while (root.presentedViewController) root = root.presentedViewController;

        // iPad requires a source anchor
        if (UI_USER_INTERFACE_IDIOM() == UIUserInterfaceIdiomPad) {
            vc.popoverPresentationController.sourceView = root.view;
            vc.popoverPresentationController.sourceRect =
                CGRectMake(root.view.bounds.size.width / 2,
                           root.view.bounds.size.height / 2, 0, 0);
            vc.popoverPresentationController.permittedArrowDirections = 0;
        }

        [root presentViewController:vc animated:YES completion:nil];
    }
}
