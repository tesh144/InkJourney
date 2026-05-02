#import <AVFoundation/AVFoundation.h>

extern "C"
{
    bool _HasTorch()
    {
        AVCaptureDevice *device = [AVCaptureDevice defaultDeviceWithMediaType:AVMediaTypeVideo];
        return device != nil && [device hasTorch];
    }

    void _SetTorchMode(bool on)
    {
        AVCaptureDevice *device = [AVCaptureDevice defaultDeviceWithMediaType:AVMediaTypeVideo];
        if (device == nil || ![device hasTorch]) return;

        NSError *error = nil;
        if ([device lockForConfiguration:&error])
        {
            device.torchMode = on ? AVCaptureTorchModeOn : AVCaptureTorchModeOff;
            [device unlockForConfiguration];
        }
    }
}
