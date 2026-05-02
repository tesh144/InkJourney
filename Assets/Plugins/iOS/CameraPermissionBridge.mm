#import <AVFoundation/AVFoundation.h>

extern "C"
{
    // 0=notDetermined  1=restricted  2=denied  3=authorized
    int _GetCameraAuthorizationStatus()
    {
        return (int)[AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo];
    }

    void _RequestCameraAuthorization(const char* gameObjectName)
    {
        NSString* objName = [NSString stringWithUTF8String:gameObjectName];
        [AVCaptureDevice requestAccessForMediaType:AVMediaTypeVideo completionHandler:^(BOOL granted) {
            dispatch_async(dispatch_get_main_queue(), ^{
                NSString* msg = [NSString stringWithFormat:@"%d", granted ? 3 : 2];
                UnitySendMessage([objName UTF8String], "OnCameraPermissionResult", [msg UTF8String]);
            });
        }];
    }
}
