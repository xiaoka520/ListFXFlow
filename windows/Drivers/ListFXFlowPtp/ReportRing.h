#pragma once
#include "Protocol.h"
#include <string.h>
/* Caller provides synchronization. Bounded, nonblocking, no allocations. */
typedef struct LFX_PTP_SLOT {
    uint64_t Sequence,Counter;
    uint32_t Length;
    uint8_t Bytes[LFX_PTP_REPORT_LIMIT];
} LFX_PTP_SLOT;
typedef struct LFX_PTP_RING {
    uint32_t Head,Count;
    uint64_t Observed,Dropped;
    LFX_PTP_SLOT Slots[LFX_PTP_RING_CAPACITY];
} LFX_PTP_RING;
static void LfxRingReset(LFX_PTP_RING *ring) {
    ring->Head=ring->Count=0;ring->Observed=ring->Dropped=0;
}
static int LfxRingPush(LFX_PTP_RING *ring,const void *bytes,uint32_t length,uint64_t counter) {
    LFX_PTP_SLOT *slot;
    if(!bytes || !length || length>LFX_PTP_REPORT_LIMIT)return 0;
    if(ring->Count==LFX_PTP_RING_CAPACITY) {
        ring->Head=(ring->Head+1)%LFX_PTP_RING_CAPACITY;ring->Count--;ring->Dropped++;
    }
    slot=&ring->Slots[(ring->Head+ring->Count)%LFX_PTP_RING_CAPACITY];
    slot->Sequence=++ring->Observed;slot->Counter=counter;slot->Length=length;
    memcpy(slot->Bytes,bytes,length);ring->Count++;return 1;
}
static uint32_t LfxRingPop(LFX_PTP_RING *ring,void *output,uint32_t capacity) {
    LFX_PTP_SLOT *slot;LFX_PTP_REPORT_HEADER header;uint32_t length;
    if(!ring->Count)return 0;
    slot=&ring->Slots[ring->Head];length=(uint32_t)sizeof(header)+slot->Length;
    if(capacity<length)return 0; /* Do not consume on a short output buffer. */
    header.Size=length;header.Version=LFX_PTP_VERSION;header.Sequence=slot->Sequence;
    header.Counter=slot->Counter;header.Length=slot->Length;header.Reserved=0;
    memcpy(output,&header,sizeof(header));memcpy((uint8_t *)output+sizeof(header),slot->Bytes,slot->Length);
    ring->Head=(ring->Head+1)%LFX_PTP_RING_CAPACITY;ring->Count--;return length;
}
