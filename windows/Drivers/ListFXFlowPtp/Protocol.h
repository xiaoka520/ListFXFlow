#pragma once
#include <stdint.h>
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
    uint32_t Size,Version,Flags,ReportLimit,DescriptorLength,InputReportLength;
    uint32_t VendorId,ProductId,Revision,RingCapacity;
    uint64_t Dropped,Observed,Frequency;
} LFX_PTP_META;
typedef struct LFX_PTP_REPORT_HEADER {
    uint32_t Size,Version;
    uint64_t Sequence,Counter;
    uint32_t Length,Reserved;
} LFX_PTP_REPORT_HEADER;
#pragma pack(pop)
