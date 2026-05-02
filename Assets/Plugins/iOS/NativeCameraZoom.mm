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
    void _SetZoomFactor(float factor, bool front)
    {
        AVCaptureDevice* device = GetDevice(front);
        if (!device) return;
        NSError* err = nil;
        if ([device lockForConfiguration:&err])
        {
            CGFloat clamped = MAX(device.minAvailableVideoZoomFactor,
                                  MIN((CGFloat)factor, device.maxAvailableVideoZoomFactor));
            device.videoZoomFactor = clamped;
            [device unlockForConfiguration];
        }
    }

    float _GetMinZoomFactor(bool front)
    {
        AVCaptureDevice* device = GetDevice(front);
        return device ? (float)device.minAvailableVideoZoomFactor : 1.0f;
    }

    float _GetMaxZoomFactor(bool front)
    {
        AVCaptureDevice* device = GetDevice(front);
        return device ? (float)device.maxAvailableVideoZoomFactor : 1.0f;
    }
}
