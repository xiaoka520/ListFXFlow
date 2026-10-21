#include <assert.h>
#include <stdlib.h>
#include <stdio.h>
#include "../../windows/Drivers/ListFXFlowPtp/ReportRing.h"
int main(void) {
    LFX_PTP_RING *ring=calloc(1,sizeof(*ring));uint8_t data[4096],output[4128];LFX_PTP_REPORT_HEADER header;
    assert(ring);assert(sizeof(LFX_PTP_META)==64);assert(sizeof(header)==32);
    for(unsigned i=0;i<sizeof(data);i++)data[i]=(uint8_t)i;
    LfxRingReset(ring);
    assert(!LfxRingPush(ring,NULL,1,0));assert(!LfxRingPush(ring,data,0,0));assert(!LfxRingPush(ring,data,4097,0));
    for(unsigned i=0;i<10000;i++)assert(LfxRingPush(ring,data,sizeof(data),i));
    assert(ring->Count==64 && ring->Dropped==9936 && ring->Observed==10000);
    assert(!LfxRingPop(ring,output,10) && ring->Count==64);
    for(unsigned i=0;i<64;i++) {
        assert(LfxRingPop(ring,output,sizeof(output))==4128);memcpy(&header,output,sizeof(header));
        assert(header.Sequence==9937+i && header.Counter==9936+i && header.Length==4096 && header.Size==4128 && header.Version==1);
        assert(!memcmp(output+32,data,4096));assert(!memcmp(data,output+32,4096));
    }
    assert(!LfxRingPop(ring,output,sizeof(output)));LfxRingReset(ring);assert(!ring->Observed && !ring->Dropped && !ring->Count);
    free(ring);puts("PASS PTP driver ABI, 10k report overflow, FIFO, short buffers, byte preservation and reset");return 0;
}
