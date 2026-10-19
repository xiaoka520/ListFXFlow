#ifndef FLOW_HID_H
#define FLOW_HID_H
#include <stdint.h>
#include <stdbool.h>
#include <CoreGraphics/CoreGraphics.h>
#include <IOKit/hidsystem/IOLLEvent.h>
// Construction is separate from posting so tests never generate user input.
typedef struct { uint32_t type, flags, options; NXEventData data; } FlowHIDKeyEvent;
FlowHIDKeyEvent FlowHIDMakeKey(uint16_t code, bool down, bool modifier, bool repeated, uint32_t flags);
FlowHIDKeyEvent FlowHIDMakeCapsNotification(bool down);
uint32_t FlowHIDOpen(void);
void FlowHIDClose(uint32_t connection);
bool FlowHIDSetCaps(uint32_t connection, bool enabled);
bool FlowHIDPostKey(uint32_t connection, FlowHIDKeyEvent event);
bool FlowPreLoginMarkerPresent(void);
bool FlowEnableBackgroundCursorControl(void);
int32_t FlowSessionPostMouse(CGEventRef _Nullable event, uint8_t * _Nullable buttons);
// Constructs data only. Does not register a device or dispatch HID input.
CFDataRef _Nullable FlowCopyTouchpadGesturePayload(uint64_t timestamp) CF_RETURNS_RETAINED;
#endif
