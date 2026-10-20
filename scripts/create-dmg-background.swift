import AppKit
import Foundation
import CoreImage

let args=CommandLine.arguments
let output=args.count>1 ? args[1]:"assets/DmgBackground.png"
let preview=args.contains("--preview")
let width:CGFloat=660,height:CGFloat=400,scale:CGFloat=2
let root=URL(fileURLWithPath:#filePath).deletingLastPathComponent().deletingLastPathComponent()
let bitmap=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:Int(width*scale),pixelsHigh:Int(height*scale),
    bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0)!
let graphics=NSGraphicsContext(bitmapImageRep:bitmap)!
let previous=NSGraphicsContext.current;NSGraphicsContext.current=graphics
let context=graphics.cgContext
context.scaleBy(x:scale,y:scale);context.interpolationQuality = .high
let canvas=NSRect(x:0,y:0,width:width,height:height)

// Restore the original midnight surface, violet glow and dot matrix.
NSGradient(starting:NSColor(calibratedRed:0.065,green:0.075,blue:0.11,alpha:1),
    ending:NSColor(calibratedRed:0.035,green:0.042,blue:0.07,alpha:1))!.draw(in:NSBezierPath(rect:canvas),angle:-90)
NSGradient(starting:NSColor(calibratedRed:0.35,green:0.32,blue:0.72,alpha:0.18),
    ending:NSColor(calibratedRed:0.35,green:0.32,blue:0.72,alpha:0))!
    .draw(in:NSBezierPath(ovalIn:NSRect(x:90,y:80,width:480,height:260)),relativeCenterPosition:.zero)
context.setFillColor(NSColor.white.withAlphaComponent(0.10).cgColor)
for x in stride(from:12.0,to:Double(width),by:18) {
    for y in stride(from:12.0,to:Double(height),by:18) {
        context.fillEllipse(in:CGRect(x:x,y:y,width:1.4,height:1.4))
    }
}

// Blur the actual backdrop at its original position. No magnification,
// displacement, specular rims, highlights, label pills or glass shadows.
context.flush()
let backdrop=bitmap.cgImage!
let gaussian=CIImage(cgImage:backdrop).clampedToExtent()
    .applyingFilter("CIGaussianBlur",parameters:[kCIInputRadiusKey:20])
    .cropped(to:CGRect(x:0,y:0,width:backdrop.width,height:backdrop.height))
let blur=NSImage(cgImage:CIContext().createCGImage(gaussian,from:gaussian.extent)!,size:NSSize(width:width,height:height))
for x:CGFloat in [90,410] {
    let area=NSBezierPath(roundedRect:NSRect(x:x,y:106,width:160,height:154),xRadius:26,yRadius:26)
    NSGraphicsContext.saveGraphicsState();area.addClip()
    blur.draw(in:canvas)
    // Finder uses black file names on image backgrounds. A diffuse light tint
    // increases toward the lower edge, without a separate label capsule.
    NSGradient(colorsAndLocations:(NSColor(calibratedWhite:0.95,alpha:0.67),0),
        (NSColor(calibratedWhite:0.95,alpha:0.27),0.40),
        (NSColor(calibratedWhite:0.95,alpha:0.13),1))!.draw(in:area,angle:90)
    NSGraphicsContext.restoreGraphicsState()
}

let accent=NSColor(calibratedRed:0.57,green:0.55,blue:1,alpha:1)
let arrow=NSBezierPath();arrow.move(to:NSPoint(x:260,y:196));arrow.line(to:NSPoint(x:400,y:196))
arrow.lineWidth=3;arrow.lineCapStyle = .round;accent.setStroke();arrow.stroke()
let head=NSBezierPath();head.move(to:NSPoint(x:386,y:184));head.line(to:NSPoint(x:402,y:196));head.line(to:NSPoint(x:386,y:208))
head.lineWidth=3;head.lineCapStyle = .round;head.lineJoinStyle = .round;accent.setStroke();head.stroke()

let centered=NSMutableParagraphStyle();centered.alignment = .center
func text(_ value:String,_ rect:NSRect,_ font:NSFont,_ color:NSColor) {
    NSAttributedString(string:value,attributes:[.font:font,.foregroundColor:color,.paragraphStyle:centered]).draw(in:rect)
}
text("ListFXFlow",NSRect(x:0,y:326,width:width,height:36),.systemFont(ofSize:28,weight:.bold),.white)
text("雷雳直连 · 键鼠与剪贴板共享",NSRect(x:0,y:297,width:width,height:22),.systemFont(ofSize:13,weight:.regular),.white.withAlphaComponent(0.58))
text("拖动 ListFXFlow 到 Applications 完成安装",NSRect(x:0,y:24,width:width,height:16),.monospacedSystemFont(ofSize:10,weight:.medium),.white.withAlphaComponent(0.65))
if preview {
    for (center,path,name) in [(CGFloat(170),root.appendingPathComponent("artifacts/ListFXFlow.app").path,"ListFXFlow.app"),(CGFloat(490),"/Applications","Applications")] {
        NSWorkspace.shared.icon(forFile:path).draw(in:NSRect(x:center-48,y:152,width:96,height:96))
        text(name,NSRect(x:center-68,y:122,width:136,height:18),.systemFont(ofSize:12),NSColor(calibratedWhite:0.08,alpha:1))
    }
}
context.flush();NSGraphicsContext.current=previous
// Encode a fresh snapshot after the immutable intermediate used by Core Image.
let final=NSBitmapImageRep(cgImage:context.makeImage()!)
final.size=NSSize(width:width,height:height)
let url=URL(fileURLWithPath:output)
try FileManager.default.createDirectory(at:url.deletingLastPathComponent(),withIntermediateDirectories:true)
try final.representation(using:.png,properties:[:])!.write(to:url)
print("Rendered \(preview ? "preview":"Finder background"): \(url.path)")
