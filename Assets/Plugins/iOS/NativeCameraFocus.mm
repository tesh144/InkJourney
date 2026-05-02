#import <AVFoundation/AVFoundation.h>

static AVCaptureDevice* GetDevice(bool front)
{
    AVCaptureDevicePosition pos = front ? AVCaptureDevicePositionFront : AVCaptureDevicePositionBack;
    AVCaptureDeviceDiscoverySession* session =
        [AVCaptureDeviceDiscoverySession
            discoverySessionWithDeviceTypes:@[AVCaptureDeviceTypeBuiltInWideAngleCamera]
            mediaType:AVMediaTypeVideo
            position:pos];
    return session.devices.firstObject;
}

extern "C"
{
    void _SetFocusPoint(float x, float y, bool front)
    {
        AVCaptureDevice* device = GetDevice(front);
        if (!device) return;

        CGPoint point = CGPointMake(x, y);

        NSError* err = nil;
        if ([device lockForConfiguration:&err])
        {
            if (device.isFocusPointOfInterestSupported)
            {
                device.focusPointOfInterest = point;
                device.focusMode = AVCaptureFocusModeAutoFocus;
            }
            if (device.isExposurePointOfInterestSupported)
            {
                device.exposurePointOfInterest = point;
                device.exposureMode = AVCaptureExposureModeAutoExpose;
            }
            [device unlockForConfiguration];
        }
    }
}
