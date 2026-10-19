import AppKit
import Foundation
import CoreImage

let output = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "assets/DmgBackground.png"
let width: CGFloat = 660, height: CGFloat = 400
let image = NSImage(size: NSSize(width: width, height: height))
image.lockFocus()
let context = NSGraphicsContext.current!.cgContext

// Dark window surface with a soft violet glow.
let background = NSBezierPath(rect: NSRect(x: 0, y: 0, width: width, height: height))
NSGradient(starting: NSColor(calibratedRed: 0.065, green: 0.075, blue: 0.11, alpha: 1),
           ending: NSColor(calibratedRed: 0.035, green: 0.042, blue: 0.07, alpha: 1))!.draw(in: background, angle: -90)
let glow = NSGradient(starting: NSColor(calibratedRed: 0.35, green: 0.32, blue: 0.72, alpha: 0.18),
                      ending: NSColor(calibratedRed: 0.35, green: 0.32, blue: 0.72, alpha: 0))!
glow.draw(in: NSBezierPath(ovalIn: NSRect(x: 90, y: 80, width: 480, height: 260)), relativeCenterPosition: NSPoint(x: 0, y: 0))

// Subtle dot matrix like the arrangement background in the app.
context.setFillColor(NSColor.white.withAlphaComponent(0.10).cgColor)
for x in stride(from: 12.0, to: Double(width), by: 18.0) {
    for y in stride(from: 12.0, to: Double(height), by: 18.0) {
        context.fillEllipse(in: CGRect(x: x, y: y, width: 1.4, height: 1.4))
    }
}

// Frosted glass uses the actual background blurred through each panel, with
// translucent tint, a refractive rim and a soft light shelf behind Finder labels.
image.unlockFocus()
let base = NSBitmapImageRep(data:image.tiffRepresentation!)!.cgImage!
let ciContext=CIContext()
let blurred=CIImage(cgImage:base).clampedToExtent().applyingFilter("CIGaussianBlur",parameters:[kCIInputRadiusKey:14])
let blurImage=NSImage(cgImage:ciContext.createCGImage(blurred,from:CGRect(x:0,y:0,width:base.width,height:base.height))!,size:NSSize(width:width,height:height))
image.lockFocus()
for x in [90.0, 410.0] {
    let rect=NSRect(x:x,y:106,width:160,height:154)
    let zone=NSBezierPath(roundedRect:rect,xRadius:26,yRadius:26)
    NSGraphicsContext.saveGraphicsState()
    let shadow=NSShadow();shadow.shadowColor=NSColor.black.withAlphaComponent(0.35);shadow.shadowBlurRadius=18;shadow.shadowOffset=NSSize(width:0,height:-8);shadow.set()
    NSColor.white.withAlphaComponent(0.10).setFill();zone.fill()
    NSGraphicsContext.restoreGraphicsState()
    NSGraphicsContext.saveGraphicsState();zone.addClip()
    blurImage.draw(in:NSRect(x:0,y:0,width:width,height:height))
    NSGradient(starting:NSColor(calibratedRed:0.62,green:0.70,blue:0.87,alpha:0.36),
               ending:NSColor(calibratedRed:0.39,green:0.44,blue:0.65,alpha:0.15))!.draw(in:zone,angle:-90)
    // A gradual milky refraction keeps Finder's native black labels readable.
    NSGradient(colorsAndLocations:(NSColor(calibratedRed:0.76,green:0.82,blue:0.94,alpha:0.72),0.0),
               (NSColor.white.withAlphaComponent(0.22),0.36),
               (NSColor.white.withAlphaComponent(0.02),1.0))!.draw(in:NSBezierPath(rect:rect),angle:90)
    NSGraphicsContext.restoreGraphicsState()
    NSColor.white.withAlphaComponent(0.26).setStroke();zone.lineWidth=1;zone.stroke()
    let inner=NSBezierPath(roundedRect:rect.insetBy(dx:1.5,dy:1.5),xRadius:24.5,yRadius:24.5)
    NSColor(calibratedRed:0.67,green:0.85,blue:1,alpha:0.10).setStroke();inner.lineWidth=1;inner.stroke()
    let highlight=NSBezierPath();highlight.move(to:NSPoint(x:x+28,y:259));highlight.line(to:NSPoint(x:x+132,y:259))
    NSColor.white.withAlphaComponent(0.50).setStroke();highlight.lineWidth=1;highlight.lineCapStyle = .round;highlight.stroke()
}

let accent = NSColor(calibratedRed: 0.57, green: 0.55, blue: 1, alpha: 1)
let arrow = NSBezierPath()
arrow.move(to: NSPoint(x: 260, y: 196)); arrow.line(to: NSPoint(x: 400, y: 196))
arrow.lineWidth = 3; arrow.lineCapStyle = .round; accent.setStroke(); arrow.stroke()
let head = NSBezierPath()
head.move(to: NSPoint(x: 386, y: 184)); head.line(to: NSPoint(x: 402, y: 196)); head.line(to: NSPoint(x: 386, y: 208))
head.lineWidth = 3; head.lineCapStyle = .round; head.lineJoinStyle = .round; accent.setStroke(); head.stroke()

let titleStyle = NSMutableParagraphStyle(); titleStyle.alignment = .center
let title = NSAttributedString(string: "ListFXFlow", attributes: [
    .font: NSFont.systemFont(ofSize: 28, weight: .bold), .foregroundColor: NSColor.white,
    .paragraphStyle: titleStyle
])
title.draw(in: NSRect(x: 0, y: 326, width: width, height: 36))
let subtitle = NSAttributedString(string: "雷雳直连 · 键鼠与剪贴板共享", attributes: [
    .font: NSFont.systemFont(ofSize: 13, weight: .regular), .foregroundColor: NSColor.white.withAlphaComponent(0.58),
    .paragraphStyle: titleStyle
])
subtitle.draw(in: NSRect(x: 0, y: 297, width: width, height: 22))
let version = NSAttributedString(string: "拖动 ListFXFlow 到 Applications 完成安装", attributes: [
    .font: NSFont.monospacedSystemFont(ofSize: 10, weight: .medium), .foregroundColor: NSColor.white.withAlphaComponent(0.65),
    .paragraphStyle: titleStyle
])
version.draw(in: NSRect(x: 0, y: 24, width: width, height: 16))

image.unlockFocus()
let rep = NSBitmapImageRep(data: image.tiffRepresentation!)!
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: output))
