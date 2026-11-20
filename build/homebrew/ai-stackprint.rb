# Homebrew cask template for AI Stackprint (macOS, Apple silicon). Not published yet: it belongs in a tap (krurkrit/homebrew-tap, Casks/ai-stackprint.rb).
# Fill in the three marked values from a real release, then test with: brew install --cask ./ai-stackprint.rb
cask "ai-stackprint" do
  version "0.0.0" # the release without the leading "v"
  sha256 "REPLACE_WITH_SHA256_OF_THE_PKG" # shasum -a 256 AiStackprint-osx-Setup.pkg

  url "https://github.com/krurkrit/ai-stackprint/releases/download/v#{version}/AiStackprint-osx-Setup.pkg"
  name "AI Stackprint"
  desc "Inventory of AI apps, skills, plugins, configuration files and logs on this machine"
  homepage "https://github.com/krurkrit/ai-stackprint"

  depends_on arch: :arm64

  pkg "AiStackprint-osx-Setup.pkg"

  uninstall pkgutil: "REPLACE_WITH_PKG_ID", # pkgutil --pkgs | grep -i stackprint, after an install
            delete:  "~/Applications/AiStackprint.app"

  zap trash: "~/.ai-stackprint"
end
