#import <UserNotifications/UserNotifications.h>

extern "C"
{
    // 0=notDetermined  1=denied  2=authorized
    void _GetNotificationStatus(const char* gameObjectName)
    {
        NSString* objName = [NSString stringWithUTF8String:gameObjectName];
        [[UNUserNotificationCenter currentNotificationCenter]
            getNotificationSettingsWithCompletionHandler:^(UNNotificationSettings* settings) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    int status = (int)settings.authorizationStatus;
                    // Treat provisional (3) and ephemeral (4) as authorized
                    if (status > 2) status = 2;
                    NSString* msg = [NSString stringWithFormat:@"%d", status];
                    UnitySendMessage([objName UTF8String], "OnNotificationStatusResult", [msg UTF8String]);
                });
            }];
    }

    void _RequestNotificationPermission(const char* gameObjectName)
    {
        NSString* objName = [NSString stringWithUTF8String:gameObjectName];
        UNAuthorizationOptions options =
            UNAuthorizationOptionAlert | UNAuthorizationOptionBadge | UNAuthorizationOptionSound;
        [[UNUserNotificationCenter currentNotificationCenter]
            requestAuthorizationWithOptions:options
            completionHandler:^(BOOL granted, NSError* error) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    NSString* msg = [NSString stringWithFormat:@"%d", granted ? 2 : 1];
                    UnitySendMessage([objName UTF8String], "OnNotificationStatusResult", [msg UTF8String]);
                });
            }];
    }
}
