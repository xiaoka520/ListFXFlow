#pragma once
/* Kernel builds use WDK integer types; importing the user-mode MSVC stdint
 * pulls a conflicting CRT into KMDF with recent Visual Studio toolchains. */
#ifdef _NTDDK_
typedef UCHAR LFX_U8;
typedef ULONG LFX_U32;
typedef ULONGLONG LFX_U64;
#else
#include <stdint.h>
typedef uint8_t LFX_U8;
typedef uint32_t LFX_U32;
typedef uint64_t LFX_U64;
#endif
#define LFX_PTP_VERSION 1u
#define LFX_PTP_REPORT_LIMIT 4096u
#define LFX_PTP_DESCRIPTOR_LIMIT 65536u
#define LFX_PTP_RING_CAPACITY 64u
/* METHOD_BUFFERED, FILE_READ_ACCESS. No write, routing or suppression IOCTL. */
#define LFX_PTP_META_IOCTL 0x00226000u
#define LFX_PTP_DESCRIPTOR_IOCTL 0x00226004u
#define LFX_PTP_REPORT_IOCTL 0x00226008u
#define LFX_PTP_FLAG_VALIDATED 1u
#define LFX_PTP_FLAG_OBSERVE_ONLY 2u
/* {3BCFD8E1-210C-41F5-B092-BE38A2AA5D0B} */
#define LFX_PTP_INTERFACE_GUID {0x3bcfd8e1,0x210c,0x41f5,{0xb0,0x92,0xbe,0x38,0xa2,0xaa,0x5d,0x0b}}
#define LFX_PTP_RAW_CLASS_GUID {0x3bcfd8e2,0x210c,0x41f5,{0xb0,0x92,0xbe,0x38,0xa2,0xaa,0x5d,0x0b}}
#pragma pack(push,8)
typedef struct LFX_PTP_META {
    LFX_U32 Size,Version,Flags,ReportLimit,DescriptorLength,InputReportLength;
    LFX_U32 VendorId,ProductId,Revision,RingCapacity;
    LFX_U64 Dropped,Observed,Frequency;
} LFX_PTP_META;
typedef struct LFX_PTP_REPORT_HEADER {
    LFX_U32 Size,Version;
    LFX_U64 Sequence,Counter;
    LFX_U32 Length,Reserved;
} LFX_PTP_REPORT_HEADER;
#pragma pack(pop)
