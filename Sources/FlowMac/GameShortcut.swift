struct GameShortcut {
    private var held=false,captured=false
    mutating func process(down:Bool,chord:Bool,available:Bool)->(consume:Bool,toggle:Bool) {
        if !down { let consume=captured;held=false;captured=false;return(consume,false) }
        if held { return(captured,false) }
        held=true;captured=chord && available;return(captured,captured)
    }
}
