import CoreGraphics

public enum PointerParking {
    public static func anchor(sample:CGPoint,bounds:CGRect,side:Side)->CGPoint {
        let x=min(bounds.maxX-1,max(bounds.minX,sample.x.isFinite ? sample.x : bounds.midX))
        let y=min(bounds.maxY-1,max(bounds.minY,sample.y.isFinite ? sample.y : bounds.midY))
        switch side {
        case .left:return CGPoint(x:bounds.minX,y:y)
        case .right:return CGPoint(x:bounds.maxX-1,y:y)
        case .top:return CGPoint(x:x,y:bounds.minY)
        case .bottom:return CGPoint(x:x,y:bounds.maxY-1)
        }
    }
}
