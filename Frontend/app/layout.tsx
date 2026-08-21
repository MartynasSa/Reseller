import type { Metadata } from 'next'
import './globals.css'

export const metadata: Metadata = {
  title: 'Reseller',
  description: 'Marketplace alerts for resellers.',
}

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  )
}
