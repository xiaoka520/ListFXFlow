import Foundation
let image = URL(fileURLWithPath: CommandLine.arguments[1])
let volume = URL(fileURLWithPath: CommandLine.arguments[2], isDirectory: true)
let data = try image.bookmarkData(options: [.suitableForBookmarkFile], includingResourceValuesForKeys: nil, relativeTo: volume)
try data.write(to: URL(fileURLWithPath: CommandLine.arguments[3]))
var stale = false
let resolved = try URL(resolvingBookmarkData: data, options: [.withoutUI, .withoutMounting], relativeTo: volume, bookmarkDataIsStale: &stale)
guard resolved.standardizedFileURL == image.standardizedFileURL, !stale else { fatalError("Background bookmark does not resolve to the packaged image") }
