#import <AppTrackingTransparency/AppTrackingTransparency.h>
#import <Foundation/Foundation.h>

extern "C"
{
    void _RequestTrackingAuthorization(const char* gameObjectName)
    {
        if (@available(iOS 14.0, *))
        {
            NSString* objName = [NSString stringWithUTF8String:gameObjectName];
            [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    NSString* msg = [NSString stringWithFormat:@"%d", (int)status];
                    UnitySendMessage([objName UTF8String], "OnATTResponse", [msg UTF8String]);
                });
            }];
        }
        else
        {
            // Pre-iOS 14 — tracking permitted by default
            UnitySendMessage(gameObjectName, "OnATTResponse", "3");
        }
    }

    int _GetTrackingAuthorizationStatus()
    {
        if (@available(iOS 14.0, *))
            return (int)[ATTrackingManager trackingAuthorizationStatus];
        return 3;
    }
}
