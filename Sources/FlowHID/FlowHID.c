#include "FlowHID.h"
#include <IOKit/IOKitLib.h>
#include <IOKit/hidsystem/IOHIDLib.h>
#include <IOKit/hidsystem/IOHIDShared.h>
#include <IOKit/hidsystem/IOHIDParameter.h>
#include <IOKit/hidsystem/ev_keymap.h>
#include <mach-o/dyld.h>
#include <mach-o/getsect.h>
#include <math.h>
#include <dlfcn.h>

// Like Deskflow's OSXComputer::hideCursor, allow this WindowServer connection
// to control the cursor without activating a window or taking keyboard focus.
// These optional SPI symbols are resolved at runtime; parking still runs when
// a future OS does not provide them.
bool FlowEnableBackgroundCursorControl(void) {
    typedef int32_t (*ConnectionFunction)(void);
    typedef CGError (*PropertyFunction)(int32_t, int32_t, CFStringRef, CFTypeRef);
    ConnectionFunction connection=(ConnectionFunction)dlsym(RTLD_DEFAULT,"_CGSDefaultConnection");
    PropertyFunction property=(PropertyFunction)dlsym(RTLD_DEFAULT,"CGSSetConnectionProperty");
    if(!connection || !property)return false;
    int32_t cid=connection();
    return cid!=0 && property(cid,cid,CFSTR("SetsCursorInBackground"),kCFBooleanTrue)==kCGErrorSuccess;
}

// ThunderDisplay's LoginWindow Quartz marker identifies the graphical connection.
// Ordinary event-post and monitoring permission are still required.
__attribute__((used, section("__CGPreLoginApp,__cgpreloginapp")))
static const char FlowPreLoginMarker[] = "";
bool FlowPreLoginMarkerPresent(void) {
    unsigned long size=0;
    const struct mach_header_64 *header=(const struct mach_header_64 *)_dyld_get_image_header(0);
    return header && getsectiondata(header,"__CGPreLoginApp","__cgpreloginapp",&size) && size>0;
}
int32_t FlowSessionPostMouse(CGEventRef event,uint8_t *buttons) {
    if(!event || !buttons)return kCGErrorIllegalArgument;
    CGEventType type=CGEventGetType(event);
    bool down=type==kCGEventLeftMouseDown || type==kCGEventRightMouseDown || type==kCGEventOtherMouseDown;
    bool up=type==kCGEventLeftMouseUp || type==kCGEventRightMouseUp || type==kCGEventOtherMouseUp;
    bool motion=type==kCGEventMouseMoved || type==kCGEventLeftMouseDragged || type==kCGEventRightMouseDragged || type==kCGEventOtherMouseDragged;
    if(!down && !up && !motion)return kCGErrorIllegalArgument;
    CGPoint point=CGEventGetLocation(event);
    if(!isfinite(point.x) || !isfinite(point.y))return kCGErrorIllegalArgument;
    uint8_t next=*buttons;
    if(down || up) {
        int64_t button=CGEventGetIntegerValueField(event,kCGMouseEventButtonNumber);
        if(button<0 || button>2) { CGEventPost(kCGSessionEventTap,event);return kCGErrorSuccess; }
        if(down)next|=(uint8_t)(1u<<button);else next&=(uint8_t)~(1u<<button);
    }
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    CGError result=CGPostMouseEvent(point,true,3,(next&1)!=0,(next&2)!=0,(next&4)!=0);
#pragma clang diagnostic pop
    if(result==kCGErrorSuccess)*buttons=next;
    return result;
}

FlowHIDKeyEvent FlowHIDMakeCapsNotification(bool down) {
    FlowHIDKeyEvent event = {0};
    // Caps also has a system-defined notification. A flagsChanged event only
    // describes modifier state; it is not the complete physical Caps sequence.
    event.type = NX_SYSDEFINED;
    event.data.compound.subType = NX_SUBTYPE_AUX_CONTROL_BUTTONS;
    event.data.compound.misc.L[0] = (NX_KEYTYPE_CAPS_LOCK << 16) |
                                  ((down ? NX_KEYDOWN : NX_KEYUP) << 8);
    return event;
}

FlowHIDKeyEvent FlowHIDMakeKey(uint16_t code, bool down, bool modifier, bool repeated, uint32_t flags) {
    FlowHIDKeyEvent event = {0};
    event.type = modifier ? NX_FLAGSCHANGED : (down ? NX_KEYDOWN : NX_KEYUP);
    // Only modifier transitions change driver-global flags. Regular keys inherit
    // the driver's current state, allowing its normal layout/IME translation.
    event.flags = modifier ? flags : 0;
    event.options = modifier ? kIOHIDSetGlobalEventFlags : 0;
    // Never supply text: the active keyboard layout/IME owns translation and composition.
    // keyCode is required on flagsChanged too (zero would be interpreted as the A key).
    event.data.key.keyCode = code;
    event.data.key.repeat = repeated ? 1 : 0;
    return event;
}
uint32_t FlowHIDOpen(void) {
    io_service_t service = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching(kIOHIDSystemClass));
    if (!service) return 0;
    io_connect_t connection = 0;
    kern_return_t result = IOServiceOpen(service, mach_task_self(), kIOHIDParamConnectType, &connection);
    IOObjectRelease(service);
    return result == KERN_SUCCESS ? connection : 0;
}
void FlowHIDClose(uint32_t connection) { if (connection) IOServiceClose(connection); }
bool FlowHIDPostKey(uint32_t connection, FlowHIDKeyEvent event) {
    if (!connection) return false;
    // Quartz Unicode payloads bypass IME composition. Keep this compatibility API
    // isolated until macOS offers an equally usable public HID keyboard replacement.
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    kern_return_t result = IOHIDPostEvent(connection, event.type, (IOGPoint){0,0}, &event.data,
                                        kNXEventDataVersion, event.flags, event.options);
#pragma clang diagnostic pop
    return result == KERN_SUCCESS;
}

bool FlowHIDSetCaps(uint32_t connection, bool enabled) {
    return connection && IOHIDSetModifierLockState(connection, kIOHIDCapsLockState, enabled) == KERN_SUCCESS;
}
