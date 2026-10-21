/* Read-only PTP observation filter. It never edits, drops, completes early or
 * originates a hardware input report. Sideband raw PDO pattern: Microsoft's
 * Windows-driver-samples/input/kbfiltr (MIT), see THIRD_PARTY_NOTICES.txt. */
#include <ntddk.h>
#include <wdf.h>
#include <hidport.h>
#include <hidclass.h>
#include <hidusage.h>
#include <hidpi.h>
#include <ntstrsafe.h>
#include <wdmsec.h>
#include "ReportRing.h"

static const GUID InterfaceGuid=LFX_PTP_INTERFACE_GUID;
static const GUID RawClassGuid=LFX_PTP_RAW_CLASS_GUID;
typedef struct DEVICE_CONTEXT {
    WDFSPINLOCK Lock;
    WDFQUEUE SideQueue;
    WDFMEMORY Descriptor;
    ULONG DescriptorLength,InputLength;
    USHORT Vendor,Product,Revision;
    BOOLEAN Ready,Reading;
    LFX_PTP_RING Ring;
} DEVICE_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT,Context)
typedef struct CHILD_CONTEXT { WDFDEVICE Parent;WDFQUEUE ParentQueue; } CHILD_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(CHILD_CONTEXT,ChildContext)
DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD DeviceAdd;
EVT_WDF_DEVICE_PREPARE_HARDWARE PrepareHardware;
EVT_WDF_IO_QUEUE_IO_READ ReadInput;
EVT_WDF_REQUEST_COMPLETION_ROUTINE ReadComplete;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL SideIoctl;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL ChildIoctl;
EVT_WDF_DEVICE_FILE_CREATE OpenReader;
EVT_WDF_FILE_CLEANUP CloseReader;

VOID ReadInput(WDFQUEUE queue,WDFREQUEST request,size_t length) {
    WDFDEVICE device=WdfIoQueueGetDevice(queue);
    UNREFERENCED_PARAMETER(length);
    WdfRequestFormatRequestUsingCurrentType(request);
    WdfRequestSetCompletionRoutine(request,ReadComplete,device);
    if(!WdfRequestSend(request,WdfDeviceGetIoTarget(device),WDF_NO_SEND_OPTIONS))
        WdfRequestComplete(request,WdfRequestGetStatus(request));
}
VOID ReadComplete(WDFREQUEST request,WDFIOTARGET target,PWDF_REQUEST_COMPLETION_PARAMS params,WDFCONTEXT raw) {
    DEVICE_CONTEXT *ctx=Context((WDFDEVICE)raw);
    WDFMEMORY memory;PVOID buffer;size_t length=0;
    UNREFERENCED_PARAMETER(target);
    if(NT_SUCCESS(params->IoStatus.Status) && params->IoStatus.Information>0 && params->IoStatus.Information<=LFX_PTP_REPORT_LIMIT &&
       NT_SUCCESS(WdfRequestRetrieveOutputMemory(request,&memory))) {
        buffer=WdfMemoryGetBuffer(memory,&length);
        if(length>=params->IoStatus.Information) {
            WdfSpinLockAcquire(ctx->Lock);
            if(ctx->Ready && ctx->Reading)
                (void)LfxRingPush(&ctx->Ring,buffer,(LFX_U32)params->IoStatus.Information,(LFX_U64)KeQueryPerformanceCounter(NULL).QuadPart);
            WdfSpinLockRelease(ctx->Lock);
        }
    }
    /* The original status, information and output bytes are always preserved. */
    WdfRequestCompleteWithInformation(request,params->IoStatus.Status,params->IoStatus.Information);
}
NTSTATUS PrepareHardware(WDFDEVICE device,WDFCMRESLIST raw,WDFCMRESLIST translated) {
    DEVICE_CONTEXT *ctx=Context(device);HID_COLLECTION_INFORMATION info={0};HIDP_CAPS caps;
    WDF_MEMORY_DESCRIPTOR output;WDF_OBJECT_ATTRIBUTES attrs;WDFMEMORY descriptor=NULL;
    WDF_REQUEST_SEND_OPTIONS options;ULONG_PTR returned=0;PVOID data;NTSTATUS status;
    UNREFERENCED_PARAMETER(raw);UNREFERENCED_PARAMETER(translated);
    if(ctx->Ready)return STATUS_SUCCESS;
    WDF_REQUEST_SEND_OPTIONS_INIT(&options,WDF_REQUEST_SEND_OPTION_TIMEOUT);
    WDF_REQUEST_SEND_OPTIONS_SET_TIMEOUT(&options,WDF_REL_TIMEOUT_IN_MS(1000));
    WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(&output,&info,sizeof(info));
    status=WdfIoTargetSendIoctlSynchronously(WdfDeviceGetIoTarget(device),NULL,IOCTL_HID_GET_COLLECTION_INFORMATION,NULL,&output,&options,&returned);
    if(!NT_SUCCESS(status) || returned<sizeof(info) || !info.DescriptorSize || info.DescriptorSize>LFX_PTP_DESCRIPTOR_LIMIT)return STATUS_SUCCESS;
    WDF_OBJECT_ATTRIBUTES_INIT(&attrs);attrs.ParentObject=device;
    status=WdfMemoryCreate(&attrs,NonPagedPoolNx,'pXFL',info.DescriptorSize,&descriptor,&data);
    if(!NT_SUCCESS(status))return STATUS_SUCCESS;
    WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(&output,data,info.DescriptorSize);returned=0;
    status=WdfIoTargetSendIoctlSynchronously(WdfDeviceGetIoTarget(device),NULL,IOCTL_HID_GET_COLLECTION_DESCRIPTOR,NULL,&output,&options,&returned);
    if(NT_SUCCESS(status) && returned==info.DescriptorSize && HidP_GetCaps((PHIDP_PREPARSED_DATA)data,&caps)==HIDP_STATUS_SUCCESS &&
       caps.UsagePage==0x0d && caps.Usage==0x05 && caps.InputReportByteLength>0 && caps.InputReportByteLength<=LFX_PTP_REPORT_LIMIT) {
        ctx->Descriptor=descriptor;ctx->DescriptorLength=info.DescriptorSize;ctx->InputLength=caps.InputReportByteLength;
        ctx->Vendor=info.VendorID;ctx->Product=info.ProductID;ctx->Revision=info.VersionNumber;
        WdfSpinLockAcquire(ctx->Lock);ctx->Ready=TRUE;WdfSpinLockRelease(ctx->Lock);
    } else WdfObjectDelete(descriptor);
    /* Failure of an optional observer must not fail the original touchpad. */
    return STATUS_SUCCESS;
}
VOID OpenReader(WDFDEVICE child,WDFREQUEST request,WDFFILEOBJECT file) {
    DEVICE_CONTEXT *ctx=Context(ChildContext(child)->Parent);
    UNREFERENCED_PARAMETER(file);
    WdfSpinLockAcquire(ctx->Lock);LfxRingReset(&ctx->Ring);ctx->Reading=TRUE;WdfSpinLockRelease(ctx->Lock);
    WdfRequestComplete(request,STATUS_SUCCESS);
}
VOID CloseReader(WDFFILEOBJECT file) {
    DEVICE_CONTEXT *ctx=Context(ChildContext(WdfFileObjectGetDevice(file))->Parent);
    WdfSpinLockAcquire(ctx->Lock);ctx->Reading=FALSE;LfxRingReset(&ctx->Ring);WdfSpinLockRelease(ctx->Lock);
}
VOID ChildIoctl(WDFQUEUE queue,WDFREQUEST request,size_t outLength,size_t inLength,ULONG code) {
    WDF_REQUEST_FORWARD_OPTIONS options;NTSTATUS status;
    UNREFERENCED_PARAMETER(outLength);UNREFERENCED_PARAMETER(inLength);
    if(code!=LFX_PTP_META_IOCTL && code!=LFX_PTP_DESCRIPTOR_IOCTL && code!=LFX_PTP_REPORT_IOCTL) {
        WdfRequestComplete(request,STATUS_INVALID_DEVICE_REQUEST);return;
    }
    WDF_REQUEST_FORWARD_OPTIONS_INIT(&options);
    status=WdfRequestForwardToParentDeviceIoQueue(request,ChildContext(WdfIoQueueGetDevice(queue))->ParentQueue,&options);
    if(!NT_SUCCESS(status))WdfRequestComplete(request,status);
}
VOID SideIoctl(WDFQUEUE queue,WDFREQUEST request,size_t outLength,size_t inLength,ULONG code) {
    DEVICE_CONTEXT *ctx=Context(WdfIoQueueGetDevice(queue));PVOID buffer=NULL;size_t capacity=0;NTSTATUS status;
    ULONG_PTR written=0;LARGE_INTEGER frequency;LFX_PTP_META meta={0};BOOLEAN ready;
    UNREFERENCED_PARAMETER(inLength);UNREFERENCED_PARAMETER(outLength);
    status=WdfRequestRetrieveOutputBuffer(request,1,&buffer,&capacity);
    if(!NT_SUCCESS(status)) { WdfRequestComplete(request,status);return; }
    switch(code) {
    case LFX_PTP_META_IOCTL:
        if(capacity<sizeof(meta)) { status=STATUS_BUFFER_TOO_SMALL;break; }
        (void)KeQueryPerformanceCounter(&frequency);
        meta.Size=sizeof(meta);meta.Version=LFX_PTP_VERSION;meta.Flags=LFX_PTP_FLAG_OBSERVE_ONLY;
        meta.ReportLimit=LFX_PTP_REPORT_LIMIT;meta.RingCapacity=LFX_PTP_RING_CAPACITY;meta.Frequency=(LFX_U64)frequency.QuadPart;
        WdfSpinLockAcquire(ctx->Lock);
        if(ctx->Ready)meta.Flags|=LFX_PTP_FLAG_VALIDATED;
        meta.DescriptorLength=ctx->DescriptorLength;meta.InputReportLength=ctx->InputLength;
        meta.VendorId=ctx->Vendor;meta.ProductId=ctx->Product;meta.Revision=ctx->Revision;
        meta.Dropped=ctx->Ring.Dropped;meta.Observed=ctx->Ring.Observed;
        WdfSpinLockRelease(ctx->Lock);
        RtlCopyMemory(buffer,&meta,sizeof(meta));written=sizeof(meta);break;
    case LFX_PTP_DESCRIPTOR_IOCTL:
        WdfSpinLockAcquire(ctx->Lock);ready=ctx->Ready;WdfSpinLockRelease(ctx->Lock);
        if(!ready) { status=STATUS_DEVICE_NOT_READY;break; }
        if(capacity<ctx->DescriptorLength) { status=STATUS_BUFFER_TOO_SMALL;break; }
        RtlCopyMemory(buffer,WdfMemoryGetBuffer(ctx->Descriptor,NULL),ctx->DescriptorLength);written=ctx->DescriptorLength;break;
    case LFX_PTP_REPORT_IOCTL:
        WdfSpinLockAcquire(ctx->Lock);
        if(capacity<LFX_PTP_REPORT_LIMIT+sizeof(LFX_PTP_REPORT_HEADER))status=STATUS_BUFFER_TOO_SMALL;
        else { written=LfxRingPop(&ctx->Ring,buffer,(LFX_U32)min(capacity,MAXULONG));if(!written)status=STATUS_NO_MORE_ENTRIES; }
        WdfSpinLockRelease(ctx->Lock);break;
    default:status=STATUS_INVALID_DEVICE_REQUEST;break;
    }
    WdfRequestCompleteWithInformation(request,status,written);
}
static NTSTATUS CreateSideband(WDFDEVICE parent) {
    PWDFDEVICE_INIT init=WdfPdoInitAllocate(parent);WDFDEVICE child;WDF_OBJECT_ATTRIBUTES attrs;
    WDF_IO_QUEUE_CONFIG queue;WDF_FILEOBJECT_CONFIG file;WDF_DEVICE_PNP_CAPABILITIES caps;NTSTATUS status;
    UNICODE_STRING id=RTL_CONSTANT_STRING(L"ListFXFlow\\PtpObserver");
    UNICODE_STRING instance=RTL_CONSTANT_STRING(L"Observer");
    UNICODE_STRING description=RTL_CONSTANT_STRING(L"ListFXFlow PTP read-only observer");
    UNICODE_STRING location=RTL_CONSTANT_STRING(L"Precision Touchpad report sideband");
    if(!init)return STATUS_INSUFFICIENT_RESOURCES;
    status=WdfPdoInitAssignRawDevice(init,&RawClassGuid);if(!NT_SUCCESS(status))goto fail;
    status=WdfDeviceInitAssignSDDLString(init,&SDDL_DEVOBJ_SYS_ALL_ADM_ALL);if(!NT_SUCCESS(status))goto fail;
    WdfDeviceInitSetExclusive(init,TRUE);WdfPdoInitAllowForwardingRequestToParent(init);
    status=WdfPdoInitAssignDeviceID(init,&id);if(!NT_SUCCESS(status))goto fail;
    status=WdfPdoInitAssignInstanceID(init,&instance);if(!NT_SUCCESS(status))goto fail;
    status=WdfPdoInitAddDeviceText(init,&description,&location,0x409);if(!NT_SUCCESS(status))goto fail;
    WdfPdoInitSetDefaultLocale(init,0x409);
    WDF_FILEOBJECT_CONFIG_INIT(&file,OpenReader,NULL,CloseReader);
    WdfDeviceInitSetFileObjectConfig(init,&file,WDF_NO_OBJECT_ATTRIBUTES);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attrs,CHILD_CONTEXT);attrs.ExecutionLevel=WdfExecutionLevelPassive;
    status=WdfDeviceCreate(&init,&attrs,&child);if(!NT_SUCCESS(status))goto fail;
    ChildContext(child)->Parent=parent;ChildContext(child)->ParentQueue=Context(parent)->SideQueue;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queue,WdfIoQueueDispatchParallel);queue.PowerManaged=WdfFalse;queue.EvtIoDeviceControl=ChildIoctl;
    status=WdfIoQueueCreate(child,&queue,WDF_NO_OBJECT_ATTRIBUTES,NULL);if(!NT_SUCCESS(status))goto childFail;
    WDF_DEVICE_PNP_CAPABILITIES_INIT(&caps);caps.Removable=WdfFalse;caps.SurpriseRemovalOK=WdfTrue;caps.UniqueID=WdfFalse;
    WdfDeviceSetPnpCapabilities(child,&caps);
    status=WdfDeviceCreateDeviceInterface(child,&InterfaceGuid,NULL);if(!NT_SUCCESS(status))goto childFail;
    status=WdfFdoAddStaticChild(parent,child);if(NT_SUCCESS(status))return status;
childFail:WdfObjectDelete(child);return status;
fail:if(init)WdfDeviceInitFree(init);return status;
}
NTSTATUS DeviceAdd(WDFDRIVER driver,PWDFDEVICE_INIT init) {
    WDFDEVICE device;WDF_OBJECT_ATTRIBUTES attrs;WDF_IO_QUEUE_CONFIG queue;WDF_PNPPOWER_EVENT_CALLBACKS pnp;NTSTATUS status;
    UNREFERENCED_PARAMETER(driver);
    WdfFdoInitSetFilter(init);
    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnp);pnp.EvtDevicePrepareHardware=PrepareHardware;
    WdfDeviceInitSetPnpPowerEventCallbacks(init,&pnp);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attrs,DEVICE_CONTEXT);attrs.SynchronizationScope=WdfSynchronizationScopeNone;
    status=WdfDeviceCreate(&init,&attrs,&device);if(!NT_SUCCESS(status))return status;
    WDF_OBJECT_ATTRIBUTES_INIT(&attrs);attrs.ParentObject=device;
    status=WdfSpinLockCreate(&attrs,&Context(device)->Lock);if(!NT_SUCCESS(status))return status;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queue,WdfIoQueueDispatchParallel);queue.EvtIoRead=ReadInput;
    status=WdfIoQueueCreate(device,&queue,WDF_NO_OBJECT_ATTRIBUTES,NULL);if(!NT_SUCCESS(status))return status;
    WDF_IO_QUEUE_CONFIG_INIT(&queue,WdfIoQueueDispatchParallel);queue.PowerManaged=WdfFalse;queue.EvtIoDeviceControl=SideIoctl;
    WDF_OBJECT_ATTRIBUTES_INIT(&attrs);attrs.ExecutionLevel=WdfExecutionLevelPassive;
    status=WdfIoQueueCreate(device,&queue,&attrs,&Context(device)->SideQueue);if(!NT_SUCCESS(status))return status;
    /* Sideband allocation failure never prevents the original device starting. */
    (void)CreateSideband(device);
    return STATUS_SUCCESS;
}
NTSTATUS DriverEntry(PDRIVER_OBJECT object,PUNICODE_STRING path) {
    WDF_DRIVER_CONFIG config;WDF_DRIVER_CONFIG_INIT(&config,DeviceAdd);
    return WdfDriverCreate(object,path,WDF_NO_OBJECT_ATTRIBUTES,&config,WDF_NO_HANDLE);
}
C_ASSERT(sizeof(LFX_PTP_META)==64);
C_ASSERT(sizeof(LFX_PTP_REPORT_HEADER)==32);
