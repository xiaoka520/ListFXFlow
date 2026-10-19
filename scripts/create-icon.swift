import AppKit
import Foundation

let folder = URL(fileURLWithPath: CommandLine.arguments[1])
let iconset = folder.appendingPathComponent("FlowIcon.iconset")
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
for size in [16,32,128,256,512] {
    for factor in [1,2] {
        let pixels = size*factor
        let image = NSImage(size: NSSize(width: pixels,height: pixels))
        image.lockFocus()
        let context = NSGraphicsContext.current!.cgContext
        context.scaleBy(x: CGFloat(pixels)/512,y: CGFloat(pixels)/512)
        let rect = CGRect(x: 24,y: 24,width: 464,height: 464)
        let background = NSBezierPath(roundedRect: rect,xRadius: 108,yRadius: 108)
        NSGradient(starting: NSColor(red:0.22,green:0.20,blue:0.40,alpha:1),ending: NSColor(red:0.06,green:0.075,blue:0.13,alpha:1))!.draw(in: background,angle: -60)
        for (x,y,color) in [(82.0,170.0,NSColor(red:0.63,green:0.60,blue:1,alpha:1)),(255.0,214.0,NSColor(red:0.45,green:0.89,blue:0.76,alpha:1))] {
            color.setStroke(); color.withAlphaComponent(0.12).setFill()
            let screen = NSBezierPath(roundedRect: CGRect(x:x,y:y,width:176,height:126),xRadius:16,yRadius:16)
            screen.lineWidth=8; screen.fill(); screen.stroke()
            let stand=NSBezierPath(); stand.move(to:NSPoint(x:x+88,y:y)); stand.line(to:NSPoint(x:x+88,y:y-25)); stand.move(to:NSPoint(x:x+57,y:y-25)); stand.line(to:NSPoint(x:x+119,y:y-25)); stand.lineWidth=8; stand.lineCapStyle = .round; stand.stroke()
        }
        let cursor=NSBezierPath(); cursor.move(to:NSPoint(x:220,y:302)); cursor.line(to:NSPoint(x:228,y:216)); cursor.line(to:NSPoint(x:251,y:242)); cursor.line(to:NSPoint(x:276,y:215)); cursor.line(to:NSPoint(x:290,y:229)); cursor.line(to:NSPoint(x:265,y:255)); cursor.line(to:NSPoint(x:298,y:264)); cursor.close()
        NSColor.white.setFill(); cursor.fill()
        image.unlockFocus()
        let bitmap=NSBitmapImageRep(data:image.tiffRepresentation!)!
        try bitmap.representation(using:.png,properties:[:])!.write(to:iconset.appendingPathComponent("icon_\(size)x\(size)\(factor==2 ? "@2x" : "").png"))
    }
}
let process=Process(); process.executableURL=URL(fileURLWithPath:"/usr/bin/iconutil")
process.arguments=["-c","icns",iconset.path,"-o",folder.appendingPathComponent("FlowIcon.icns").path]
try process.run(); process.waitUntilExit()
guard process.terminationStatus==0 else { exit(1) }
try FileManager.default.removeItem(at:iconset)
