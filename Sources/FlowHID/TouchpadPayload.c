#include "FlowHID.h"
#include <dlfcn.h>
#include <pthread.h>

// Let the running OS serialize its own IOHID layout. Historical hand-written
// digitizer structs have incompatible sizes on modern macOS. These optional SPI
// functions create CF objects only; no device/event-system dispatch takes place.
typedef CFTypeRef (*CreateDigitizer)(CFAllocatorRef,uint64_t,uint32_t,uint32_t,uint32_t,uint32_t,uint32_t,double,double,double,double,double,uint32_t,uint32_t,uint32_t);
typedef CFTypeRef (*CreateVendor)(CFAllocatorRef,uint64_t,uint32_t,uint32_t,uint32_t,const uint8_t *,CFIndex,uint32_t);
typedef void (*AppendEvent)(CFTypeRef,CFTypeRef,uint32_t);
typedef CFDataRef (*CreateData)(CFAllocatorRef,CFTypeRef);
static CreateDigitizer digitizer;
static CreateVendor vendor;
static AppendEvent append;
static CreateData serialize;
static pthread_once_t once=PTHREAD_ONCE_INIT;
static void resolve(void) {
    digitizer=(CreateDigitizer)dlsym(RTLD_DEFAULT,"IOHIDEventCreateDigitizerEvent");
    vendor=(CreateVendor)dlsym(RTLD_DEFAULT,"IOHIDEventCreateVendorDefinedEvent");
    append=(AppendEvent)dlsym(RTLD_DEFAULT,"IOHIDEventAppendEvent");
    serialize=(CreateData)dlsym(RTLD_DEFAULT,"IOHIDEventCreateData");
}
CFDataRef FlowCopyTouchpadGesturePayload(uint64_t timestamp) {
    pthread_once(&once,resolve);
    if(!digitizer || !vendor || !append || !serialize)return NULL;
    CFTypeRef hand=digitizer(kCFAllocatorDefault,timestamp,3,0,0,0,0,0,0,0,0,0,0,0,2);
    if(!hand)return NULL;
    const uint8_t token[40]={0};
    CFTypeRef child=vendor(kCFAllocatorDefault,timestamp,0xff00,0x1777,1,token,sizeof(token),0);
    if(!child) { CFRelease(hand);return NULL; }
    append(hand,child,0);
    CFDataRef result=serialize(kCFAllocatorDefault,hand);
    CFRelease(child);CFRelease(hand);
    return result;
}
